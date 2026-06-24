namespace DietPlanner.Endpoints.Meal;

public interface IMealImportService
{
    /// <summary>
    /// Parses a CSV file of meals (header row required; columns Name, SlotKey, Kcal, ProteinG,
    /// CarbsG, FibreG, Plants, MfName, ZoeNotes, Notes) and upserts each row into the Meals table,
    /// matching existing meals by Name + SlotKey so re-uploading the same file safely updates
    /// macros instead of creating duplicates.
    /// </summary>
    Task<MealImportResult> ImportFromCsvAsync(Stream csvFileContent, CancellationToken cancellationToken);
}
