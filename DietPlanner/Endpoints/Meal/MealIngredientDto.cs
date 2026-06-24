namespace DietPlanner.Endpoints.Meal;

public sealed record MealIngredientDto(Guid IngredientId, string Name, decimal Quantity, string? Unit);
