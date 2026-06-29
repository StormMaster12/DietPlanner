namespace DietPlanner.Endpoints.DayPlan;

public interface IIngredientGroupingService
{
    /// <summary>
    /// Sends a weekly shopping list to an LLM and asks it to convert every line to grams and assign
    /// each ingredient to the supermarket aisle it would be found in.
    /// </summary>
    Task<IngredientGroupingResult> GroupIngredientsAsync(
        IReadOnlyList<DayPlanIngredientDto> ingredients, CancellationToken cancellationToken);
}
