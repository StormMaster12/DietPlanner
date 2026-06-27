namespace DietPlanner.E2ETests;

/// <summary>
/// Starts the single <see cref="DietPlannerAppFactory"/> instance shared by every test in the
/// assembly, once at the start of the run, and tears it down once at the end - the per-test cost
/// is just <see cref="DietPlannerAppFactory.ResetStateAsync"/> rather than a fresh container.
/// </summary>
[SetUpFixture]
public sealed class TestRunSetup
{
    private static DietPlannerAppFactory? s_factory;

    public static DietPlannerAppFactory Factory =>
        s_factory ?? throw new InvalidOperationException($"{nameof(TestRunSetup)} has not started the app yet.");

    [OneTimeSetUp]
    public async Task StartAppAsync()
    {
        s_factory = new DietPlannerAppFactory();
        await s_factory.StartAsync();
    }

    [OneTimeTearDown]
    public async Task StopAppAsync()
    {
        if (s_factory is not null)
        {
            await s_factory.DisposeAsync();
        }
    }
}
