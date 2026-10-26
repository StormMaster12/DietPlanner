using DietPlanner.BackgroundJobs;

namespace DietPlanner.Endpoints.Meal;

public static class IngredientNormalizationJob
{
    public static Guid StartIngredientNormalization(this IBackgroundJobRunner<IngredientNormalizationResult> jobs)
        => jobs.Start(
            "Ingredient names could not be normalized.",
            (services, cancellationToken) => services.GetRequiredService<IIngredientNormalizationService>().NormalizeIngredientNamesAsync(cancellationToken));
}
