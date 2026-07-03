using System.Text.Json;
using System.Text.Json.Serialization;
using DietPlanner.Endpoints.Meal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DietPlanner.Endpoints.DayPlan;

public sealed partial class MealAdditionSuggestionService : IMealAdditionSuggestionService
{
    private const string SystemPrompt =
        """
        You help someone close a gap in their daily fibre and/or plant-diversity targets by
        suggesting small additions to a specific meal they already eat.

        You are given the meal's name, any notes about it, its current ingredient list, and how
        many grams of fibre and/or how many distinct plants the day is currently short of its
        target. Suggest a short list (no more than 5) of realistic additions to that meal -
        extra ingredients or simple swaps - that would help close that gap. Prefer additions that
        fit naturally with the meal's existing ingredients and notes.

        Respond with ONLY a raw JSON array, no markdown fences, no commentary, in this exact shape:
        [{"ingredient": "<name>", "amount": "<a realistic serving size, e.g. \"30g\" or \"1 tbsp\">", "reason": "<one short sentence on why this helps>"}]

        If you cannot think of any reasonable addition, return an empty array.
        """;

    private readonly IAnthropicApiService _anthropicApi;
    private readonly AppDbContext _db;
    private readonly ILogger<MealAdditionSuggestionService> _logger;

    public MealAdditionSuggestionService(IAnthropicApiService anthropicApi, AppDbContext db, ILogger<MealAdditionSuggestionService> logger)
    {
        _anthropicApi = anthropicApi;
        _db = db;
        _logger = logger;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Anthropic API call failed while suggesting meal additions")]
    private partial void LogAnthropicCallFailed(Exception exception);

    public async Task<MealAdditionSuggestionResult> SuggestAdditionsAsync(
        MealAdditionSuggestionRequest request, CancellationToken cancellationToken)
    {
        string fingerprint = BuildRequestFingerprint(request);

        MealAdditionSuggestionCacheEntry? cached = await _db.MealAdditionSuggestionCache
            .FirstOrDefaultAsync(c => c.MealId == request.MealId, cancellationToken);

        if (cached is not null && cached.RequestFingerprint == fingerprint)
        {
            return new MealAdditionSuggestionResult(
                JsonSerializer.Deserialize<List<SuggestedMealAddition>>(cached.SuggestionsJson) ?? [],
                JsonSerializer.Deserialize<List<string>>(cached.ErrorsJson) ?? []);
        }

        if (!_anthropicApi.IsConfigured)
        {
            return new MealAdditionSuggestionResult([], ["Meal addition suggestions are not configured: missing Anthropic API key."]);
        }

        try
        {
            List<SuggestedAdditionResult> results = await CallAnthropicAsync(fingerprint, cancellationToken);
            List<SuggestedMealAddition> suggestions = results
                .Select(r => new SuggestedMealAddition(r.Ingredient, r.Amount, r.Reason))
                .ToList();

            var result = new MealAdditionSuggestionResult(suggestions, []);
            await SaveToCacheAsync(request.MealId, fingerprint, result, cached, cancellationToken);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            LogAnthropicCallFailed(ex);
            return new MealAdditionSuggestionResult([], [$"Could not get suggestions for '{request.MealName}': {ex.Message}"]);
        }
    }

    /// <summary>
    /// Serializes the exact payload that would be sent to the Anthropic API so it can double as
    /// both the request body and a cache-invalidation fingerprint: identical meal contents and
    /// shortfall produce an identical fingerprint, so a repeat request can skip the API call.
    /// </summary>
    private static string BuildRequestFingerprint(MealAdditionSuggestionRequest request)
    {
        var userPayload = new RequestPayload(
            request.MealName,
            request.ZoeNotes,
            request.Notes,
            request.Ingredients.Select(i => new RequestIngredient(i.Name, i.Quantity, i.Unit)).ToList(),
            request.FibreShortfallG,
            request.PlantsShortfall);
        return JsonSerializer.Serialize(userPayload);
    }

    private async Task SaveToCacheAsync(
        Guid mealId, string fingerprint, MealAdditionSuggestionResult result, MealAdditionSuggestionCacheEntry? existing,
        CancellationToken cancellationToken)
    {
        string suggestionsJson = JsonSerializer.Serialize(result.Suggestions);
        string errorsJson = JsonSerializer.Serialize(result.Errors);

        if (existing is not null)
        {
            existing.RequestFingerprint = fingerprint;
            existing.SuggestionsJson = suggestionsJson;
            existing.ErrorsJson = errorsJson;
            existing.CreatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            _db.MealAdditionSuggestionCache.Add(new MealAdditionSuggestionCacheEntry
            {
                MealId = mealId,
                RequestFingerprint = fingerprint,
                SuggestionsJson = suggestionsJson,
                ErrorsJson = errorsJson,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<SuggestedAdditionResult>> CallAnthropicAsync(string userContent, CancellationToken cancellationToken)
    {
        string json = await _anthropicApi.SendMessageAsync(
            SystemPrompt,
            userContent,
            maxTokens: 2048,
            truncatedResponseMessage: "The Anthropic response was truncated.",
            cancellationToken);

        return JsonSerializer.Deserialize<List<SuggestedAdditionResult>>(json) ?? [];
    }

    private sealed record RequestIngredient(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("quantity")] decimal Quantity,
        [property: JsonPropertyName("unit")] string? Unit);

    private sealed record RequestPayload(
        [property: JsonPropertyName("mealName")] string MealName,
        [property: JsonPropertyName("zoeNotes")] string? ZoeNotes,
        [property: JsonPropertyName("notes")] string? Notes,
        [property: JsonPropertyName("ingredients")] List<RequestIngredient> Ingredients,
        [property: JsonPropertyName("fibreShortfallG")] int FibreShortfallG,
        [property: JsonPropertyName("plantsShortfall")] int PlantsShortfall);

    private sealed record SuggestedAdditionResult(
        [property: JsonPropertyName("ingredient")] string Ingredient,
        [property: JsonPropertyName("amount")] string Amount,
        [property: JsonPropertyName("reason")] string Reason);
}
