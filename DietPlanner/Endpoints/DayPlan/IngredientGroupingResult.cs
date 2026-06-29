namespace DietPlanner.Endpoints.DayPlan;

/// <summary>
/// Outcome of asking an LLM to convert a weekly shopping list to grams and group it into
/// supermarket aisles.
/// </summary>
public sealed record IngredientGroupingResult(IReadOnlyList<GroupedIngredientDto> Ingredients, IReadOnlyList<string> Errors);
