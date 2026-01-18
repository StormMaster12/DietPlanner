using DietPlanner.Endpoints.Slots;

namespace DietPlanner.Endpoints.Meal;

public interface IMealsService
{
    Task<IReadOnlyList<MealDto>> GetMealsAsync(CancellationToken cancellationToken);
    Task<MealDto?> GetMealAsync(Guid mealId, CancellationToken cancellationToken);
    Task<(UpsertMealResult Result, MealDto? Meal)> UpsertMealAsync(UpsertMealRequest request, CancellationToken cancellationToken);
    Task<DeleteResult> DeleteMealAsync(Guid mealId, CancellationToken cancellationToken);
}
