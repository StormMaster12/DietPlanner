using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DietPlanner.Endpoints.Meal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DietPlanner.Endpoints.DayPlan;

public sealed partial class MealAdditionSuggestionService : IMealAdditionSuggestionService
{
    private const string AnthropicVersion = "2023-06-01";

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

    private readonly HttpClient _httpClient;
    private readonly AnthropicOptions _options;
    private readonly ILogger<MealAdditionSuggestionService> _logger;

    public MealAdditionSuggestionService(HttpClient httpClient, IOptions<AnthropicOptions> options, ILogger<MealAdditionSuggestionService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Anthropic API call failed while suggesting meal additions")]
    private partial void LogAnthropicCallFailed(Exception exception);

    public async Task<MealAdditionSuggestionResult> SuggestAdditionsAsync(
        MealAdditionSuggestionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return new MealAdditionSuggestionResult([], ["Meal addition suggestions are not configured: missing Anthropic API key."]);
        }

        try
        {
            List<SuggestedAdditionResult> results = await CallAnthropicAsync(request, cancellationToken);
            List<SuggestedMealAddition> suggestions = results
                .Select(r => new SuggestedMealAddition(r.Ingredient, r.Amount, r.Reason))
                .ToList();

            return new MealAdditionSuggestionResult(suggestions, []);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            LogAnthropicCallFailed(ex);
            return new MealAdditionSuggestionResult([], [$"Could not get suggestions for '{request.MealName}': {ex.Message}"]);
        }
    }

    private async Task<List<SuggestedAdditionResult>> CallAnthropicAsync(
        MealAdditionSuggestionRequest request, CancellationToken cancellationToken)
    {
        var userPayload = new RequestPayload(
            request.MealName,
            request.ZoeNotes,
            request.Notes,
            request.Ingredients.Select(i => new RequestIngredient(i.Name, i.Quantity, i.Unit)).ToList(),
            request.FibreShortfallG,
            request.PlantsShortfall);
        string userContent = JsonSerializer.Serialize(userPayload);

        var requestBody = new AnthropicRequest(
            _options.Model,
            2048,
            SystemPrompt,
            [new AnthropicMessage("user", userContent)]);

        using HttpRequestMessage httpRequest = new(HttpMethod.Post, _options.BaseUrl);
        httpRequest.Headers.Add("x-api-key", _options.ApiKey);
        httpRequest.Headers.Add("anthropic-version", AnthropicVersion);
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Anthropic API returned {(int)response.StatusCode}: {responseBody}");
        }

        AnthropicResponse? anthropicResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseBody);
        string? resultJson = anthropicResponse?.Content.FirstOrDefault(c => c.Type == "text")?.Text;

        if (string.IsNullOrWhiteSpace(resultJson))
        {
            throw new JsonException("Anthropic API returned no text content.");
        }

        if (anthropicResponse?.StopReason == "max_tokens")
        {
            throw new JsonException("The Anthropic response was truncated.");
        }

        return JsonSerializer.Deserialize<List<SuggestedAdditionResult>>(StripMarkdownFences(resultJson)) ?? [];
    }

    private static string StripMarkdownFences(string text)
    {
        string trimmed = text.Trim();
        if (!trimmed.StartsWith("```"))
        {
            return trimmed;
        }

        int firstNewLine = trimmed.IndexOf('\n');
        int fenceEnd = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewLine < 0 || fenceEnd <= firstNewLine
            ? trimmed
            : trimmed[(firstNewLine + 1)..fenceEnd].Trim();
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

    private sealed record AnthropicRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("system")] string System,
        [property: JsonPropertyName("messages")] List<AnthropicMessage> Messages);

    private sealed record AnthropicMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record AnthropicResponse(
        [property: JsonPropertyName("content")] List<AnthropicContentBlock> Content,
        [property: JsonPropertyName("stop_reason")] string? StopReason);

    private sealed record AnthropicContentBlock(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string? Text);

    private sealed record SuggestedAdditionResult(
        [property: JsonPropertyName("ingredient")] string Ingredient,
        [property: JsonPropertyName("amount")] string Amount,
        [property: JsonPropertyName("reason")] string Reason);
}
