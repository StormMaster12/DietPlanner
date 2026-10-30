namespace DietPlanner.BackgroundJobs;

/// <summary>
/// Runs slow work (in practice, Anthropic API calls) on a detached background task instead of
/// inline with the Blazor circuit or HTTP request that asked for it, since a slow LLM call can
/// outlast a SignalR/proxy timeout. Callers start a job and poll <see cref="GetStatus"/>.
/// </summary>
public interface IBackgroundJobRunner<TResult>
{
    /// <summary>
    /// Starts <paramref name="work"/> in its own DI scope and returns immediately with a job id.
    /// </summary>
    /// <param name="failureMessage">
    /// Shown to the user if <paramref name="work"/> throws; the exception itself is only logged,
    /// since its message isn't written for end users.
    /// </param>
    Guid Start(string failureMessage, Func<IServiceProvider, CancellationToken, Task<TResult>> work);

    /// <summary>
    /// Returns the current status of a previously started job, or null if no job with that id is
    /// known (never started, or evicted after completion).
    /// </summary>
    BackgroundJobStatusView<TResult>? GetStatus(Guid jobId);
}
