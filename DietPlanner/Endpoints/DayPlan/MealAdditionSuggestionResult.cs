namespace DietPlanner.Endpoints.DayPlan;

/// <summary>Outcome of asking an LLM to suggest fibre/plant-boosting additions for a meal.</summary>
public sealed record MealAdditionSuggestionResult(IReadOnlyList<SuggestedMealAddition> Suggestions, IReadOnlyList<string> Errors);
