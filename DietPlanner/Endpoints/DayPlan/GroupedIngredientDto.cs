namespace DietPlanner.Endpoints.DayPlan;

/// <summary>
/// One shopping-list ingredient after AI-assisted enrichment: its quantity converted to grams and
/// the supermarket aisle it would be found in.
/// </summary>
public sealed record GroupedIngredientDto(string Name, decimal Grams, string Category);
