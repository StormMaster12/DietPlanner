namespace DietPlanner.Endpoints.Meal;

/// <summary>One distinct raw ingredient name that the model decided to rename, and how many ingredient rows it touched.</summary>
public sealed record IngredientRename(string OriginalName, string NormalizedName, int AffectedCount);

/// <summary>
/// Outcome of running AI-assisted normalization over the distinct ingredient names already in the
/// database: which raw names were renamed to a more consistent canonical form (e.g. merging "0% fat
/// Greek yogurt" and "0% Greek yogurt" onto one spelling), and how many ingredient rows changed.
/// </summary>
public sealed record IngredientNormalizationResult(
    int DistinctNamesConsidered,
    IReadOnlyList<IngredientRename> Renames,
    IReadOnlyList<string> Errors);
