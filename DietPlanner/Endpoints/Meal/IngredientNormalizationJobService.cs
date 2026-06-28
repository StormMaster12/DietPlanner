using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DietPlanner.Endpoints.Meal;

/// <summary>
/// Runs ingredient name normalization on a detached background task instead of inline with the
/// Blazor circuit call that triggered it, for the same reason as <see cref="MealPdfImportJobService"/>:
/// the Anthropic call(s) can take longer than we'd want to hold a request/SignalR connection open
/// for. Single-user deployment, so an in-memory dictionary is enough; completed jobs are evicted
/// after a short grace period so the dictionary doesn't grow unbounded.
/// </summary>
public sealed partial class IngredientNormalizationJobService : IIngredientNormalizationJobService
{
    private static readonly TimeSpan CompletedJobRetention = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<Guid, JobEntry> _jobs = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IngredientNormalizationJobService> _logger;

    public IngredientNormalizationJobService(IServiceScopeFactory scopeFactory, ILogger<IngredientNormalizationJobService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Guid Start()
    {
        EvictExpiredJobs();

        Guid jobId = Guid.NewGuid();
        _jobs[jobId] = new JobEntry(IngredientNormalizationJobStatus.Running, null, null, null);

        _ = RunAsync(jobId);

        return jobId;
    }

    public IngredientNormalizationJobStatusView? GetStatus(Guid jobId) =>
        _jobs.TryGetValue(jobId, out JobEntry? entry)
            ? new IngredientNormalizationJobStatusView(entry.Status, entry.Result, entry.ErrorMessage)
            : null;

    private async Task RunAsync(Guid jobId)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IIngredientNormalizationService normalizationService = scope.ServiceProvider.GetRequiredService<IIngredientNormalizationService>();

            IngredientNormalizationResult result = await normalizationService.NormalizeIngredientNamesAsync(CancellationToken.None);

            _jobs[jobId] = new JobEntry(IngredientNormalizationJobStatus.Completed, result, null, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            LogBackgroundNormalizationFailed(ex, jobId);
            _jobs[jobId] = new JobEntry(IngredientNormalizationJobStatus.Failed, null, ex.Message, DateTimeOffset.UtcNow);
        }
    }

    private void EvictExpiredJobs()
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - CompletedJobRetention;
        foreach ((Guid jobId, JobEntry entry) in _jobs)
        {
            if (entry.Status != IngredientNormalizationJobStatus.Running && entry.CompletedAt < cutoff)
            {
                _jobs.TryRemove(jobId, out _);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Background ingredient normalization failed for job {JobId}")]
    private partial void LogBackgroundNormalizationFailed(Exception exception, Guid jobId);

    private sealed record JobEntry(
        IngredientNormalizationJobStatus Status,
        IngredientNormalizationResult? Result,
        string? ErrorMessage,
        DateTimeOffset? CompletedAt);
}
