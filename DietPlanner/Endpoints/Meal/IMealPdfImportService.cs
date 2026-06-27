namespace DietPlanner.Endpoints.Meal;

public interface IMealPdfImportService
{
    /// <summary>
    /// Extracts text from an uploaded PDF (e.g. a recipe sheet or meal plan written in free-form
    /// prose), uses an LLM to pull out structured meal entries, and upserts each one into the Meals
    /// table - matching existing meals by Name + SlotKey so re-uploading the same file safely
    /// updates macros instead of creating duplicates.
    /// </summary>
    Task<MealImportResult> ImportFromPdfAsync(Stream pdfFileContent, CancellationToken cancellationToken);
}
