using DietPlanner.BackgroundJobs;

namespace DietPlanner.Endpoints.DayPlan;

/// <summary>Starts the week plan page's Anthropic-backed work as background jobs.</summary>
public static class DayPlanJobs
{
    public static Guid StartSuggestAdditions(
        this IBackgroundJobRunner<MealAdditionSuggestionResult> jobs, MealAdditionSuggestionRequest request)
        => jobs.Start(
            "Could not get suggestions for this meal.",
            (services, cancellationToken) => services.GetRequiredService<IMealAdditionSuggestionService>().SuggestAdditionsAsync(request, cancellationToken));

    public static Guid StartGroupIngredients(
        this IBackgroundJobRunner<IngredientGroupingResult> jobs, DateOnly weekStartDate, IReadOnlyList<DayPlanIngredientDto> ingredients)
        => jobs.Start(
            "Could not group the shopping list.",
            (services, cancellationToken) => services.GetRequiredService<IIngredientGroupingService>().GroupIngredientsAsync(weekStartDate, ingredients, cancellationToken));
}
