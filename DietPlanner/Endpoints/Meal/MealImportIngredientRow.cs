namespace DietPlanner.Endpoints.Meal;

/// <summary>One ingredient line parsed from an imported meal row (CSV or PDF/AI extraction).</summary>
public sealed record MealImportIngredientRow(string Name, decimal Quantity, string? Unit);
