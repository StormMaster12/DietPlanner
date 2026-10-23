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

        You are given the meal's name, any notes about it, its current ingredient list for a
        single portion, and how many grams of fibre and/or how many distinct plants the day is
        currently short of its target. Suggest a short list (no more than 5) of realistic additions
        to that meal - extra ingredients or simple swaps - that would help close that gap. Prefer
        additions that fit naturally with the meal's existing ingredients and notes. Quantities are
        for a single portion of the meal.

        Respond with ONLY a raw JSON array, no markdown fences, no commentary, in this exact shape:
        [{"ingredient": "<name>", "quantity": <a positive number>, "unit": "<a unit such as \"g\" or \"tbsp\", or null for countable items such as 1 apple>", "reason": "<one short sentence on why this helps>"}]

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
        MealEntry? meal = await _db.Meals
            .AsNoTracking()
            .Include(m => m.Ingredients)
            .FirstOrDefaultAsync(m => m.Id == request.MealId, cancellationToken);

        if (meal is null)
        {
            return new MealAdditionSuggestionResult([], ["Could not get suggestions: the meal no longer exists."]);
        }

        string fingerprint = MealAdditionFingerprint.Compute(meal);

        MealAdditionSuggestionSet? saved = await _db.MealAdditionSuggestionSets
            .Include(s => s.Additions)
            .FirstOrDefaultAsync(s => s.MealId == meal.Id, cancellationToken);

        if (saved is not null && saved.MealFingerprint == fingerprint)
        {
            return new MealAdditionSuggestionResult(ToSuggestions(saved), []);
        }

        if (!_anthropicApi.IsConfigured)
        {
            return new MealAdditionSuggestionResult([], ["Meal addition suggestions are not configured: missing Anthropic API key."]);
        }

        List<SuggestedAdditionResult> results;
        try
        {
            results = await CallAnthropicAsync(BuildUserContent(meal, request), cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // Errors are only logged and shown in the moment, never saved, so the next request
            // for this meal tries the API again rather than replaying a stored failure.
            LogAnthropicCallFailed(ex);
            return new MealAdditionSuggestionResult([], [$"Could not get suggestions for '{meal.Name}': {ex.Message}"]);
        }

        List<MealSuggestedAddition> additions = results
            .Where(r => !string.IsNullOrWhiteSpace(r.Ingredient) && r.Quantity > 0)
            .Select((r, index) => new MealSuggestedAddition
            {
                MealId = meal.Id,
                SortOrder = index,
                Name = r.Ingredient.Trim(),
                Quantity = r.Quantity,
                Unit = string.IsNullOrWhiteSpace(r.Unit) ? null : r.Unit.Trim(),
                Reason = r.Reason?.Trim() ?? string.Empty,
            })
            .ToList();

        if (saved is null)
        {
            saved = new MealAdditionSuggestionSet { MealId = meal.Id, MealFingerprint = fingerprint };
            _db.MealAdditionSuggestionSets.Add(saved);
        }

        // Clearing the tracked collection deletes the previous suggestions as orphans in the same
        // SaveChanges, so a regenerated meal never ends up with a mix of old and new additions.
        saved.Additions.Clear();
        saved.Additions.AddRange(additions);
        saved.MealFingerprint = fingerprint;
        saved.CreatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return new MealAdditionSuggestionResult(ToSuggestions(saved), []);
    }

    /// <summary>Maps saved additions to the single-portion suggestions returned to callers, in the order the AI gave them.</summary>
    public static IReadOnlyList<SuggestedMealAddition> ToSuggestions(MealAdditionSuggestionSet set)
        => set.Additions
            .OrderBy(a => a.SortOrder)
            .Select(a => new SuggestedMealAddition(a.Name, a.Quantity, a.Unit, a.Reason))
            .ToList();

    private static string BuildUserContent(MealEntry meal, MealAdditionSuggestionRequest request)
    {
        var userPayload = new RequestPayload(
            meal.Name,
            meal.ZoeNotes,
            meal.Notes,
            meal.Ingredients.Select(i => new RequestIngredient(i.Name, i.Quantity, i.Unit)).ToList(),
            request.FibreShortfallG,
            request.PlantsShortfall);
        return JsonSerializer.Serialize(userPayload);
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
        [property: JsonPropertyName("quantity")] decimal Quantity,
        [property: JsonPropertyName("unit")] string? Unit,
        [property: JsonPropertyName("reason")] string? Reason);
}
