namespace DietPlanner.Endpoints.DayPlan;

public interface IIngredientGroupingService
{
    /// <summary>
    /// Sends a weekly shopping list to an LLM and asks it to convert every line to grams and assign
    /// each ingredient to the supermarket aisle it would be found in. Results are cached by
    /// <paramref name="weekStartDate"/> and reused as long as the ingredient list is unchanged.
    /// </summary>
    Task<IngredientGroupingResult> GroupIngredientsAsync(
        DateOnly weekStartDate, IReadOnlyList<DayPlanIngredientDto> ingredients, CancellationToken cancellationToken);
}
