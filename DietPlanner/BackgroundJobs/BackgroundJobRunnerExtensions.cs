namespace DietPlanner.BackgroundJobs;

public static class BackgroundJobRunnerExtensions
{
    /// <summary>
    /// Polls a job until it finishes. Used by pages that want to await a job's result in one place
    /// while the work itself still runs detached from the circuit, in its own DI scope.
    /// </summary>
    public static async Task<BackgroundJobStatusView<TResult>> WaitForCompletionAsync<TResult>(
        this IBackgroundJobRunner<TResult> jobs, Guid jobId, TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        while (true)
        {
            BackgroundJobStatusView<TResult>? status = jobs.GetStatus(jobId);
            if (status is null)
            {
                return new BackgroundJobStatusView<TResult>(BackgroundJobStatus.Failed, default, "The background job could not be found.");
            }

            if (status.Status != BackgroundJobStatus.Running)
            {
                return status;
            }

            await Task.Delay(pollInterval, cancellationToken);
        }
    }
}
