namespace DietPlanner.Endpoints.DayPlan;

public interface IMealAdditionSuggestionService
{
    /// <summary>
    /// Sends a meal's name, notes and ingredients to an LLM, along with how far the day is below
    /// its fibre/plant targets, and asks for a short list of additions that would close the gap.
    /// </summary>
    Task<MealAdditionSuggestionResult> SuggestAdditionsAsync(
        MealAdditionSuggestionRequest request, CancellationToken cancellationToken);
}
