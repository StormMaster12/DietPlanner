namespace DietPlanner.Endpoints.Meal;

public enum IngredientNormalizationJobStatus
{
    Running,
    Completed,
    Failed,
}

/// <summary>
/// Snapshot of a background ingredient normalization job, returned by
/// <see cref="IIngredientNormalizationJobService.GetStatus"/> for the upload page to poll.
/// <see cref="Result"/> is only populated once <see cref="Status"/> is
/// <see cref="IngredientNormalizationJobStatus.Completed"/>; <see cref="ErrorMessage"/> only once
/// it's <see cref="IngredientNormalizationJobStatus.Failed"/>.
/// </summary>
public sealed record IngredientNormalizationJobStatusView(
    IngredientNormalizationJobStatus Status,
    IngredientNormalizationResult? Result,
    string? ErrorMessage);
