namespace DietPlanner.Endpoints.DayPlan;

/// <summary>An LLM-suggested ingredient addition for a meal, with the reasoning behind it.</summary>
public sealed record SuggestedMealAddition(string Ingredient, string Amount, string Reason);
