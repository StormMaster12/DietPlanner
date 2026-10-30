namespace DietPlanner.BackgroundJobs;

public enum BackgroundJobStatus
{
    Running,
    Completed,
    Failed,
}

/// <summary>
/// Snapshot of a background job, returned by <see cref="IBackgroundJobRunner{TResult}.GetStatus"/>
/// for a page or API client to poll. <see cref="Result"/> is only populated once <see cref="Status"/>
/// is <see cref="BackgroundJobStatus.Completed"/>; <see cref="ErrorMessage"/> only once it's
/// <see cref="BackgroundJobStatus.Failed"/>.
/// </summary>
public sealed record BackgroundJobStatusView<TResult>(
    BackgroundJobStatus Status,
    TResult? Result,
    string? ErrorMessage);
