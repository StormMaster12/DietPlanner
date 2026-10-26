using DietPlanner.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DietPlanner.Tests;

[TestFixture]
public sealed class BackgroundJobRunnerTests
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    private ServiceProvider _services = null!;
    private BackgroundJobRunner<int> _runner = null!;

    [SetUp]
    public void SetUp()
    {
        _services = new ServiceCollection().BuildServiceProvider();
        _runner = new BackgroundJobRunner<int>(
            _services.GetRequiredService<IServiceScopeFactory>(), NullLogger<BackgroundJobRunner<int>>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
    }

    [Test]
    public async Task Start_ReportsRunningUntilTheWorkFinishes_ThenItsResult()
    {
        TaskCompletionSource<int> work = new();

        Guid jobId = _runner.Start("failed", (_, _) => work.Task);

        Assert.That(_runner.GetStatus(jobId)?.Status, Is.EqualTo(BackgroundJobStatus.Running));

        work.SetResult(42);
        BackgroundJobStatusView<int> status = await _runner.WaitForCompletionAsync(jobId, PollInterval, CancellationToken.None);

        Assert.That(status.Status, Is.EqualTo(BackgroundJobStatus.Completed));
        Assert.That(status.Result, Is.EqualTo(42));
        Assert.That(status.ErrorMessage, Is.Null);
    }

    [Test]
    public async Task Start_WhenTheWorkThrows_ReportsTheCallersFailureMessageNotTheExceptionMessage()
    {
        Guid jobId = _runner.Start("Friendly failure.", (_, _) => throw new InvalidOperationException("internal detail"));

        BackgroundJobStatusView<int> status = await _runner.WaitForCompletionAsync(jobId, PollInterval, CancellationToken.None);

        Assert.That(status.Status, Is.EqualTo(BackgroundJobStatus.Failed));
        Assert.That(status.ErrorMessage, Is.EqualTo("Friendly failure."));
    }

    [Test]
    public void GetStatus_ForUnknownJob_ReturnsNull()
    {
        Assert.That(_runner.GetStatus(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task WaitForCompletionAsync_ForUnknownJob_ReportsFailure()
    {
        BackgroundJobStatusView<int> status = await _runner.WaitForCompletionAsync(Guid.NewGuid(), PollInterval, CancellationToken.None);

        Assert.That(status.Status, Is.EqualTo(BackgroundJobStatus.Failed));
    }
}
