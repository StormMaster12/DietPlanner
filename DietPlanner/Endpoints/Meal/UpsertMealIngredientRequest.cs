namespace DietPlanner.Endpoints.Meal;

public sealed record UpsertMealIngredientRequest(string Name, decimal Quantity, string? Unit);
