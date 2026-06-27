using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace DietPlanner.E2ETests;

/// <summary>
/// Base class for browser-driven tests: reuses the single app instance started once for the whole
/// run by <see cref="TestRunSetup"/>, resetting its state before each test, and exposes
/// <see cref="GoToAsync"/> for navigating relative to the running server. Tests share a container
/// and database, so they must run sequentially rather than in parallel.
/// </summary>
public abstract class PageTestBase : PageTest
{
    private DietPlannerAppFactory _factory = null!;

    [SetUp]
    public async Task ResetAppStateAsync()
    {
        _factory = TestRunSetup.Factory;
        await _factory.ResetStateAsync();
    }

    public override BrowserNewContextOptions ContextOptions()
    {
        BrowserNewContextOptions options = base.ContextOptions();
        options.HttpCredentials = new HttpCredentials
        {
            Username = DietPlannerAppFactory.TestUsername,
            Password = DietPlannerAppFactory.TestPassword,
        };
        return options;
    }

    /// <summary>
    /// Stubs the next call the app makes to the (WireMock-backed) Anthropic API with the given raw
    /// response body, so PDF-import tests don't depend on the real Anthropic API.
    /// </summary>
    protected Task StubAnthropicResponseAsync(string anthropicResponseBody) =>
        _factory.StubAnthropicResponseAsync(anthropicResponseBody);

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
