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
            MealEntry? existingMeal = await db.Meals.SingleOrDefaultAsync(
                m => m.SlotKey == row.SlotKey && m.Name.ToLower() == row.Name.ToLower(),
                cancellationToken);

            if (existingMeal is null)
            {
                db.Meals.Add(new MealEntry
                {
                    Id = Guid.NewGuid(),
                    Name = row.Name,
                    SlotKey = row.SlotKey,
                    Kcal = row.Kcal,
                    ProteinG = row.ProteinG,
                    CarbsG = row.CarbsG,
                    FibreG = row.FibreG,
                    Plants = row.Plants,
                    MfName = row.MfName,
                    ZoeNotes = row.ZoeNotes,
                    Notes = row.Notes
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
                updatedCount++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return new MealImportResult(insertedCount, updatedCount, rowErrors);
    }
}
