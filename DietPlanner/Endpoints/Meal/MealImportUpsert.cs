using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Endpoints.Meal;

/// <summary>
/// Shared upsert logic for meal import rows, regardless of where they were parsed from (CSV, PDF,
/// etc). Matches existing meals by Name + SlotKey so re-importing the same data safely updates
/// macros instead of creating duplicates.
/// </summary>
internal static class MealImportUpsert
{
    public static async Task<MealImportResult> UpsertRowsAsync(
        AppDbContext db,
        List<MealImportRow> parsedRows,
        List<string> rowErrors,
        CancellationToken cancellationToken)
    {
        int insertedCount = 0;
        int updatedCount = 0;

        foreach (MealImportRow row in parsedRows)
        {
            MealEntry? existingMeal = await db.Meals
                .Include(m => m.Ingredients)
                .SingleOrDefaultAsync(
                    m => m.SlotKey == row.SlotKey && m.Name.ToLower() == row.Name.ToLower(),
                    cancellationToken);

            if (existingMeal is null)
            {
                Guid mealId = Guid.NewGuid();
                db.Meals.Add(new MealEntry
                {
                    Id = mealId,
                    Name = row.Name,
                    SlotKey = row.SlotKey,
                    Kcal = row.Kcal,
                    ProteinG = row.ProteinG,
                    CarbsG = row.CarbsG,
                    FibreG = row.FibreG,
                    Plants = row.Plants,
                    MfName = row.MfName,
                    ZoeNotes = row.ZoeNotes,
                    Notes = row.Notes,
                    Ingredients = ToIngredientEntities(row.Ingredients, mealId)
                });
                insertedCount++;
            }
            else
            {
                existingMeal.Kcal = row.Kcal;
                existingMeal.ProteinG = row.ProteinG;
                existingMeal.CarbsG = row.CarbsG;
                existingMeal.FibreG = row.FibreG;
                existingMeal.Plants = row.Plants;
                existingMeal.MfName = row.MfName;
                existingMeal.ZoeNotes = row.ZoeNotes;
                existingMeal.Notes = row.Notes;

                if (row.Ingredients is not null)
                {
                    // Same Clear() + explicit AddRange to the DbSet as MealsService.UpsertMealAsync -
                    // existingMeal is already tracked, so EF would otherwise mistake the new rows'
                    // client-set Guid keys for existing rows and mark them Modified instead of Added.
                    existingMeal.Ingredients.Clear();
                    db.MealIngredients.AddRange(ToIngredientEntities(row.Ingredients, existingMeal.Id));
                }

                updatedCount++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return new MealImportResult(insertedCount, updatedCount, rowErrors);
    }

    private static List<MealIngredient> ToIngredientEntities(IReadOnlyList<MealImportIngredientRow>? ingredients, Guid mealId)
        => (ingredients ?? []).Select(i => new MealIngredient
        {
            Id = Guid.NewGuid(),
            MealId = mealId,
            Name = i.Name,
            Quantity = i.Quantity,
            Unit = i.Unit
        }).ToList();
}
