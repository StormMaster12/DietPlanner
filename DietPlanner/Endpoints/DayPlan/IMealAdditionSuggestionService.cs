namespace DietPlanner.Endpoints.DayPlan;

public interface IMealAdditionSuggestionService
{
    /// <summary>
    /// Returns the additions already saved for this meal if the meal hasn't changed since they were
    /// generated. Otherwise sends the meal's name, notes and ingredients to an LLM, along with how
    /// far the day is below its fibre/plant targets, and saves the additions it suggests in place of
    /// any previous ones. Quantities are for a single portion of the meal.
    /// </summary>
    Task<MealAdditionSuggestionResult> SuggestAdditionsAsync(
        MealAdditionSuggestionRequest request, CancellationToken cancellationToken);
}
