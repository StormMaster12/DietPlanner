using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DietPlanner.Endpoints.Meal;

/// <summary>
/// Runs PDF meal imports on a detached background task instead of inline with the Blazor circuit
/// call that triggered them. Large PDFs need several chunked Anthropic calls and can take far
/// longer than any SignalR/HTTP timeout we'd want to hold a request open for, so the upload page
/// starts a job here and polls <see cref="GetStatus"/> instead of awaiting the import directly.
/// Single-user deployment, so an in-memory dictionary (rather than a persisted queue) is enough;
/// completed jobs are evicted after a short grace period so the dictionary doesn't grow unbounded.
/// </summary>
public sealed partial class MealPdfImportJobService : IMealPdfImportJobService
{
    private static readonly TimeSpan CompletedJobRetention = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<Guid, JobEntry> _jobs = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MealPdfImportJobService> _logger;

    public MealPdfImportJobService(IServiceScopeFactory scopeFactory, ILogger<MealPdfImportJobService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Guid Start(byte[] pdfFileContent)
    {
        EvictExpiredJobs();

        Guid jobId = Guid.NewGuid();
        _jobs[jobId] = new JobEntry(MealPdfImportJobStatus.Running, null, null, null);

        _ = RunAsync(jobId, pdfFileContent);

        return jobId;
    }

    public MealPdfImportJobStatusView? GetStatus(Guid jobId) =>
        _jobs.TryGetValue(jobId, out JobEntry? entry)
            ? new MealPdfImportJobStatusView(entry.Status, entry.Result, entry.ErrorMessage)
            : null;

    private async Task RunAsync(Guid jobId, byte[] pdfFileContent)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IMealPdfImportService importService = scope.ServiceProvider.GetRequiredService<IMealPdfImportService>();

            using MemoryStream pdfStream = new(pdfFileContent);
            MealImportResult result = await importService.ImportFromPdfAsync(pdfStream, CancellationToken.None);

            _jobs[jobId] = new JobEntry(MealPdfImportJobStatus.Completed, result, null, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            LogBackgroundImportFailed(ex, jobId);
            _jobs[jobId] = new JobEntry(MealPdfImportJobStatus.Failed, null, ex.Message, DateTimeOffset.UtcNow);
        }
    }

    private void EvictExpiredJobs()
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - CompletedJobRetention;
        foreach ((Guid jobId, JobEntry entry) in _jobs)
        {
            if (entry.Status != MealPdfImportJobStatus.Running && entry.CompletedAt < cutoff)
            {
                _jobs.TryRemove(jobId, out _);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Background PDF meal import failed for job {JobId}")]
    private partial void LogBackgroundImportFailed(Exception exception, Guid jobId);

    private sealed record JobEntry(
        MealPdfImportJobStatus Status,
        MealImportResult? Result,
        string? ErrorMessage,
        DateTimeOffset? CompletedAt);
}
