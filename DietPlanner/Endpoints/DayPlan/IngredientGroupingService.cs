using System.Text.Json;
using System.Text.Json.Serialization;
using DietPlanner.Endpoints.Meal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DietPlanner.Endpoints.DayPlan;

public sealed partial class IngredientGroupingService : IIngredientGroupingService
{
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

    private readonly IAnthropicApiService _anthropicApi;
    private readonly AppDbContext _db;
    private readonly ILogger<IngredientGroupingService> _logger;

    public IngredientGroupingService(IAnthropicApiService anthropicApi, AppDbContext db, ILogger<IngredientGroupingService> logger)
    {
        _anthropicApi = anthropicApi;
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

        if (!_anthropicApi.IsConfigured)
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

        var groupingResult = new IngredientGroupingResult(grouped, errors);

        if (errors.Count == 0)
        {
            await SaveToCacheAsync(weekStartDate, fingerprint, groupingResult, cached, cancellationToken);
        }

        return groupingResult;
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

        string json = await _anthropicApi.SendMessageAsync(
            SystemPrompt,
            userContent,
            maxTokens: 8192,
            truncatedResponseMessage: "The Anthropic response was truncated because the shopping list was too large to fit in a single reply.",
            cancellationToken);

        return JsonSerializer.Deserialize<List<GroupedLineResult>>(json) ?? [];
    }

    private sealed record RequestLine(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("quantity")] decimal Quantity,
        [property: JsonPropertyName("unit")] string? Unit);

    private sealed record GroupedLineResult(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("grams")] decimal Grams,
        [property: JsonPropertyName("category")] string Category);
}
