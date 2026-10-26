using System.Collections.Concurrent;

namespace DietPlanner.BackgroundJobs;

/// <summary>
/// Single-user deployment, so an in-memory dictionary (rather than a persisted queue) is enough;
/// completed jobs are evicted after a short grace period so the dictionary doesn't grow unbounded.
/// Registered as an open-generic singleton, so there's one job table per result type.
/// </summary>
public sealed class BackgroundJobRunner<TResult> : IBackgroundJobRunner<TResult>
{
    private static readonly TimeSpan CompletedJobRetention = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<Guid, JobEntry> _jobs = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackgroundJobRunner<TResult>> _logger;

    public BackgroundJobRunner(IServiceScopeFactory scopeFactory, ILogger<BackgroundJobRunner<TResult>> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Guid Start(string failureMessage, Func<IServiceProvider, CancellationToken, Task<TResult>> work)
    {
        EvictExpiredJobs();

        Guid jobId = Guid.NewGuid();
        _jobs[jobId] = new JobEntry(new BackgroundJobStatusView<TResult>(BackgroundJobStatus.Running, default, null), null);

        // Task.Run so none of the work (e.g. synchronous PDF text extraction) runs on the calling
        // circuit's synchronization context before its first await.
        _ = Task.Run(() => RunAsync(jobId, failureMessage, work));

        return jobId;
    }

    public BackgroundJobStatusView<TResult>? GetStatus(Guid jobId) =>
        _jobs.TryGetValue(jobId, out JobEntry? entry) ? entry.View : null;

    private async Task RunAsync(Guid jobId, string failureMessage, Func<IServiceProvider, CancellationToken, Task<TResult>> work)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            TResult result = await work(scope.ServiceProvider, CancellationToken.None);
            _jobs[jobId] = new JobEntry(new BackgroundJobStatusView<TResult>(BackgroundJobStatus.Completed, result, null), DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background job {JobId} producing {ResultType} failed", jobId, typeof(TResult).Name);
            _jobs[jobId] = new JobEntry(new BackgroundJobStatusView<TResult>(BackgroundJobStatus.Failed, default, failureMessage), DateTimeOffset.UtcNow);
        }
    }

    private void EvictExpiredJobs()
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - CompletedJobRetention;
        foreach ((Guid jobId, JobEntry entry) in _jobs)
        {
            if (entry.CompletedAt < cutoff)
            {
                _jobs.TryRemove(jobId, out _);
            }
        }
    }

    private sealed record JobEntry(BackgroundJobStatusView<TResult> View, DateTimeOffset? CompletedAt);
}
