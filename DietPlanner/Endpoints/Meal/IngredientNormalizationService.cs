using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DietPlanner.Endpoints.Meal;

public sealed partial class IngredientNormalizationService : IIngredientNormalizationService
{
    private const string SystemPrompt = """
        You clean up a list of raw ingredient names pulled from a home-cooked recipe database. Many
        entries are near-duplicates of each other that should share one consistent spelling, e.g.
        "0% fat Greek yogurt" and "0% Greek yogurt" should both become the same canonical name, as
        should "garlic powder" entered with different capitalization, or "egg white" vs "egg whites".

        Rules:
        - Merge names that clearly refer to the exact same base ingredient, just written
          inconsistently (wording order, capitalization, singular/plural, minor punctuation,
          abbreviations). Pick whichever variant reads most naturally as the canonical spelling,
          using sentence case (capitalize only the first word and proper nouns, e.g. "Greek yogurt",
          "Cajun seasoning").
        - Also merge fat-content/leanness variants and cooking-state variants of the same base
          ingredient onto the plain/base name, since these are still fundamentally the same shopping
          item, e.g. "ground turkey" and "extra lean ground turkey" should both become "Ground
          turkey"; "chicken breast" and "grilled chicken breast" should both become "Chicken breast".
        - Do NOT merge ingredients that are genuinely different foods or different cuts/forms with a
          materially different culinary identity, e.g. keep "garlic", "garlic cloves" and "minced
          garlic" distinct, keep "chicken breast" and "chicken thigh" distinct.
        - If a name is already fine as-is, or has no clear duplicate in the list, return it unchanged
          (still include it in the output).
        - Every input name must appear exactly once in the output, in the same order.

        Respond with ONLY a raw JSON array, one object per input name, no markdown fences, no
        commentary, in this exact shape:
        [{"original": "<the exact input name>", "normalized": "<the canonical name to use instead>"}]
        """;

    private const int MaxNamesPerChunk = 150;

    private readonly AppDbContext _db;
    private readonly IAnthropicApiService _anthropicApi;
    private readonly ILogger<IngredientNormalizationService> _logger;

    public IngredientNormalizationService(AppDbContext db, IAnthropicApiService anthropicApi, ILogger<IngredientNormalizationService> logger)
    {
        _db = db;
        _anthropicApi = anthropicApi;
        _logger = logger;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Anthropic API call failed while normalizing ingredient names")]
    private partial void LogAnthropicCallFailed(Exception exception);

    public async Task<IngredientNormalizationResult> NormalizeIngredientNamesAsync(CancellationToken cancellationToken)
    {
        List<string> distinctNames = await _db.MealIngredients
            .Select(i => i.Name)
            .Distinct()
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);

        if (distinctNames.Count == 0)
        {
            return new IngredientNormalizationResult(0, [], []);
        }

        if (!_anthropicApi.IsConfigured)
        {
            return new IngredientNormalizationResult(distinctNames.Count, [], ["Ingredient normalization is not configured: missing Anthropic API key."]);
        }

        var errors = new List<string>();
        var normalizedByOriginal = new Dictionary<string, string>();

        List<List<string>> chunks = distinctNames
            .Chunk(MaxNamesPerChunk)
            .Select(chunk => chunk.ToList())
            .ToList();

        for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
        {
            try
            {
                List<NormalizedNamePair> pairs = await CallAnthropicAsync(chunks[chunkIndex], cancellationToken);
                foreach (NormalizedNamePair pair in pairs)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Original) && !string.IsNullOrWhiteSpace(pair.Normalized))
                    {
                        normalizedByOriginal[pair.Original] = pair.Normalized.Trim();
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                LogAnthropicCallFailed(ex);
                string part = chunks.Count > 1 ? $"part {chunkIndex + 1} of {chunks.Count} of " : "";
                errors.Add($"Could not normalize {part}the ingredient list: {ex.Message}");
            }
        }

        List<IngredientRename> renames = await ApplyRenamesAsync(normalizedByOriginal, cancellationToken);

        return new IngredientNormalizationResult(distinctNames.Count, renames, errors);
    }

    private async Task<List<IngredientRename>> ApplyRenamesAsync(
        Dictionary<string, string> normalizedByOriginal, CancellationToken cancellationToken)
    {
        var renames = new List<IngredientRename>();

        // Only act on names the model actually changed - copying every ingredient name back even when
        // unchanged would mean rewriting (and re-saving) every row in the table for no reason.
        var actualRenames = normalizedByOriginal
            .Where(kvp => !string.Equals(kvp.Key, kvp.Value, StringComparison.Ordinal))
            .ToList();

        if (actualRenames.Count == 0)
        {
            return renames;
        }

        List<string> originalNames = actualRenames.Select(kvp => kvp.Key).ToList();
        List<MealIngredient> matchingIngredients = await _db.MealIngredients
            .Where(i => originalNames.Contains(i.Name))
            .ToListAsync(cancellationToken);

        foreach ((string originalName, string normalizedName) in actualRenames)
        {
            List<MealIngredient> affected = matchingIngredients.Where(i => i.Name == originalName).ToList();
            if (affected.Count == 0)
            {
                continue;
            }

            foreach (MealIngredient ingredient in affected)
            {
                ingredient.Name = normalizedName;
            }

            renames.Add(new IngredientRename(originalName, normalizedName, affected.Count));
        }

        if (renames.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return renames;
    }

    private async Task<List<NormalizedNamePair>> CallAnthropicAsync(List<string> names, CancellationToken cancellationToken)
    {
        string userContent = JsonSerializer.Serialize(names);

        string json = await _anthropicApi.SendMessageAsync(
            SystemPrompt,
            userContent,
            maxTokens: 8192,
            truncatedResponseMessage: "The Anthropic response was truncated because the ingredient list was too large to fit in a single reply.",
            cancellationToken);

        return JsonSerializer.Deserialize<List<NormalizedNamePair>>(json) ?? [];
    }

    private sealed record NormalizedNamePair(
        [property: JsonPropertyName("original")] string Original,
        [property: JsonPropertyName("normalized")] string Normalized);
}
