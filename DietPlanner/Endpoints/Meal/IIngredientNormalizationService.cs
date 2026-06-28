namespace DietPlanner.Endpoints.Meal;

public interface IIngredientNormalizationService
{
    /// <summary>
    /// Sends every distinct ingredient name currently in the database to an LLM and asks it to merge
    /// near-duplicate spellings (different wording, capitalization, ordering) onto a single canonical
    /// name, then rewrites matching <see cref="MealIngredient"/> rows in place.
    /// </summary>
    Task<IngredientNormalizationResult> NormalizeIngredientNamesAsync(CancellationToken cancellationToken);
}
