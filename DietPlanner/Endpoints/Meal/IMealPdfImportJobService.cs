namespace DietPlanner.Endpoints.Meal;

public interface IMealPdfImportJobService
{
    /// <summary>
    /// Queues a PDF import to run on a background task (detached from the calling circuit) and
    /// returns immediately with a job id to poll via <see cref="GetStatus"/>.
    /// </summary>
    Guid Start(byte[] pdfFileContent);

    /// <summary>
    /// Returns the current status of a previously started job, or null if no job with that id is
    /// known (never started, or evicted after completion).
    /// </summary>
    MealPdfImportJobStatusView? GetStatus(Guid jobId);
}
