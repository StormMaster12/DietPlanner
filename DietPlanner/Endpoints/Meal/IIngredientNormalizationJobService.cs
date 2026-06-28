namespace DietPlanner.Endpoints.Meal;

public interface IIngredientNormalizationJobService
{
    /// <summary>
    /// Queues an ingredient name normalization run on a background task (detached from the calling
    /// circuit) and returns immediately with a job id to poll via <see cref="GetStatus"/>.
    /// </summary>
    Guid Start();

    /// <summary>
    /// Returns the current status of a previously started job, or null if no job with that id is
    /// known (never started, or evicted after completion).
    /// </summary>
    IngredientNormalizationJobStatusView? GetStatus(Guid jobId);
}
