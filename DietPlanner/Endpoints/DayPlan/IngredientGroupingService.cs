using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DietPlanner.Endpoints.Meal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DietPlanner.Endpoints.DayPlan;

public sealed partial class IngredientGroupingService : IIngredientGroupingService
{
    private const string AnthropicVersion = "2023-06-01";

    /// <summary>Fixed aisle list the model must pick from, so the UI can group/sort consistently.</summary>
    public static readonly IReadOnlyList<string> Categories =
    [
        "Produce",
        "Dairy & Eggs",
        "Meat & Poultry",
        "Seafood",
        "Bakery & Bread",
        "Pantry & Dry Goods",
        "Canned & Jarred Goods",
        "Spices & Seasonings",
        "Condiments & Sauces",
        "Frozen",
        "Beverages",
        "Other",
    ];

    private static readonly string SystemPrompt =
        """
        You turn a home cook's weekly shopping list into something they can use in a supermarket.

        You are given a JSON array of ingredient lines, each with an index, a name, a quantity and a
        unit (the unit may be null for countable items, e.g. "2 Onion"). For every line:

        1. Convert the quantity to grams, using realistic culinary knowledge of that specific
           ingredient's density and typical size:
           - For volume units (cup, tbsp, tsp) use the density of THAT ingredient, not a generic
             liquid - e.g. a cup of flour is much lighter than a cup of Greek yogurt.
           - For weight units already in g/oz/lb, just convert (1 oz = 28.35g, 1 lb = 453.6g).
           - For countable items with no unit (e.g. "12 Apple", "10 Whole eggs", "1.5 Onion"), use
             the typical weight of one average unit of that specific item (e.g. one large egg is
             about 50g, one medium onion is about 110g, one medium apple is about 180g).
           - For a quantity of 0 (e.g. "to taste" items), return 0 grams.
           - Always return a single number of grams, rounded to the nearest whole gram. Never leave
             a line unconverted.
        2. Assign the ingredient to exactly one supermarket aisle from this fixed list:
        """
        + " " + string.Join(", ", Categories.Select(c => $"\"{c}\"")) +
        """


        Respond with ONLY a raw JSON array, one object per input line, no markdown fences, no
        commentary, in this exact shape:
        [{"index": <the input index>, "grams": <integer>, "category": "<one of the aisles above>"}]

        Every input index must appear exactly once in the output, in the same order.
        """;

    private const int MaxLinesPerChunk = 150;

    private readonly HttpClient _httpClient;
    private readonly AnthropicOptions _options;
    private readonly AppDbContext _db;
    private readonly ILogger<IngredientGroupingService> _logger;

    public IngredientGroupingService(
        HttpClient httpClient, IOptions<AnthropicOptions> options, AppDbContext db, ILogger<IngredientGroupingService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _db = db;
        _logger = logger;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Anthropic API call failed while grouping the shopping list")]
    private partial void LogAnthropicCallFailed(Exception exception);

    public async Task<IngredientGroupingResult> GroupIngredientsAsync(
        DateOnly weekStartDate, IReadOnlyList<DayPlanIngredientDto> ingredients, CancellationToken cancellationToken)
    {
        if (ingredients.Count == 0)
        {
            return new IngredientGroupingResult([], []);
        }

        string fingerprint = JsonSerializer.Serialize(ingredients);

        ShoppingListGroupingCacheEntry? cached = await _db.ShoppingListGroupingCache
            .FirstOrDefaultAsync(c => c.WeekStartDate == weekStartDate, cancellationToken);

        if (cached is not null && cached.RequestFingerprint == fingerprint)
        {
            return new IngredientGroupingResult(
                JsonSerializer.Deserialize<List<GroupedIngredientDto>>(cached.GroupedJson) ?? [],
                JsonSerializer.Deserialize<List<string>>(cached.ErrorsJson) ?? []);
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return new IngredientGroupingResult([], ["Shopping list grouping is not configured: missing Anthropic API key."]);
        }

        var errors = new List<string>();
        var grouped = new List<GroupedIngredientDto>();

        List<List<(int Index, DayPlanIngredientDto Ingredient)>> chunks = ingredients
            .Select((ingredient, index) => (Index: index, Ingredient: ingredient))
            .Chunk(MaxLinesPerChunk)
            .Select(chunk => chunk.ToList())
            .ToList();

        foreach (List<(int Index, DayPlanIngredientDto Ingredient)> chunk in chunks)
        {
            try
            {
                List<GroupedLineResult> results = await CallAnthropicAsync(chunk, cancellationToken);
                Dictionary<int, GroupedLineResult> resultsByIndex = results.ToDictionary(r => r.Index);

                foreach ((int index, DayPlanIngredientDto ingredient) in chunk)
                {
                    if (resultsByIndex.TryGetValue(index, out GroupedLineResult? result))
                    {
                        string category = Categories.Contains(result.Category) ? result.Category : "Other";
                        grouped.Add(new GroupedIngredientDto(ingredient.Name, result.Grams, category));
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                LogAnthropicCallFailed(ex);
                errors.Add($"Could not group part of the shopping list: {ex.Message}");
            }
        }

        var result = new IngredientGroupingResult(grouped, errors);

        if (errors.Count == 0)
        {
            await SaveToCacheAsync(weekStartDate, fingerprint, result, cached, cancellationToken);
        }

        return result;
    }

    private async Task SaveToCacheAsync(
        DateOnly weekStartDate, string fingerprint, IngredientGroupingResult result, ShoppingListGroupingCacheEntry? existing,
        CancellationToken cancellationToken)
    {
        string groupedJson = JsonSerializer.Serialize(result.Ingredients);
        string errorsJson = JsonSerializer.Serialize(result.Errors);

        if (existing is not null)
        {
            existing.RequestFingerprint = fingerprint;
            existing.GroupedJson = groupedJson;
            existing.ErrorsJson = errorsJson;
            existing.CreatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            _db.ShoppingListGroupingCache.Add(new ShoppingListGroupingCacheEntry
            {
                WeekStartDate = weekStartDate,
                RequestFingerprint = fingerprint,
                GroupedJson = groupedJson,
                ErrorsJson = errorsJson,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<GroupedLineResult>> CallAnthropicAsync(
        List<(int Index, DayPlanIngredientDto Ingredient)> chunk, CancellationToken cancellationToken)
    {
        var requestLines = chunk.Select(c => new RequestLine(c.Index, c.Ingredient.Name, c.Ingredient.Quantity, c.Ingredient.Unit)).ToList();
        string userContent = JsonSerializer.Serialize(requestLines);

        var requestBody = new AnthropicRequest(
            _options.Model,
            8192,
            SystemPrompt,
            [new AnthropicMessage("user", userContent)]);

        using HttpRequestMessage request = new(HttpMethod.Post, _options.BaseUrl);
        request.Headers.Add("x-api-key", _options.ApiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
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
            throw new JsonException("The Anthropic response was truncated because the shopping list was too large to fit in a single reply.");
        }

        return JsonSerializer.Deserialize<List<GroupedLineResult>>(StripMarkdownFences(resultJson)) ?? [];
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

    private sealed record RequestLine(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("quantity")] decimal Quantity,
        [property: JsonPropertyName("unit")] string? Unit);

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

    private sealed record GroupedLineResult(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("grams")] decimal Grams,
        [property: JsonPropertyName("category")] string Category);
}
