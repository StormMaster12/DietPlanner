using DietPlanner.BackgroundJobs;

namespace DietPlanner.Endpoints.Meal;

public static class MealPdfImportJob
{
    /// <summary>
    /// Large PDFs need several chunked Anthropic calls, so both the upload page and the API run the
    /// import as a background job and poll for its result.
    /// </summary>
    public static Guid StartPdfImport(this IBackgroundJobRunner<MealImportResult> jobs, byte[] pdfFileContent)
        => jobs.Start(
            "The PDF could not be imported. Check that it's a valid, unencrypted PDF file.",
            async (services, cancellationToken) =>
            {
                using MemoryStream pdfStream = new(pdfFileContent);
                return await services.GetRequiredService<IMealPdfImportService>().ImportFromPdfAsync(pdfStream, cancellationToken);
            });
}
