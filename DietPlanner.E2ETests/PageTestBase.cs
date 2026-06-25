using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace DietPlanner.E2ETests;

/// <summary>
/// Base class for browser-driven tests: starts a fresh app instance (and database) per test
/// class and exposes <see cref="GoToAsync"/> for navigating relative to the running server.
/// </summary>
[Parallelizable(ParallelScope.Self)]
public abstract class PageTestBase : PageTest
{
    private DietPlannerAppFactory _factory = null!;

    [SetUp]
    public async Task CreateAppFactoryAsync()
    {
        _factory = new DietPlannerAppFactory();
        await _factory.StartAsync();
    }

    [TearDown]
    public async Task DisposeAppFactoryAsync()
    {
        await _factory.DisposeAsync();
    }

    /// <summary>
    /// Navigates relative to the running server, then waits for network idle so the Blazor Server
    /// SignalR circuit has finished connecting before the caller starts interacting with the page
    /// - clicking too early silently no-ops because the circuit isn't wired up yet.
    /// </summary>
    protected async Task<IResponse?> GoToAsync(string relativePath)
    {
        IResponse? response = await Page.GotoAsync($"{_factory.ServerAddress}/{relativePath}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        return response;
    }
}
