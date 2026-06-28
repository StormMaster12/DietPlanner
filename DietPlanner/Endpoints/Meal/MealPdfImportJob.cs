namespace DietPlanner.Endpoints.Meal;

public enum MealPdfImportJobStatus
{
    Running,
    Completed,
    Failed,
}

/// <summary>
/// Snapshot of a background PDF import job, returned by <see cref="IMealPdfImportJobService.GetStatus"/>
/// for the upload page to poll. <see cref="Result"/> is only populated once <see cref="Status"/> is
/// <see cref="MealPdfImportJobStatus.Completed"/>; <see cref="ErrorMessage"/> only once it's
/// <see cref="MealPdfImportJobStatus.Failed"/>.
/// </summary>
public sealed record MealPdfImportJobStatusView(
    MealPdfImportJobStatus Status,
    MealImportResult? Result,
    string? ErrorMessage);
