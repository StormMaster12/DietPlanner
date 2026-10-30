using System.Net.Http.Json;
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

    protected Task ClearAnthropicStubsAsync() => _factory.ClearAnthropicStubsAsync();

    protected Task<IReadOnlyList<string>> GetAnthropicRequestBodiesAsync() => _factory.GetAnthropicRequestBodiesAsync();

    /// <summary>
    /// Creates (or replaces) a meal through the app's own API, so tests that aren't about meal
    /// entry can set up data without driving the meals page.
    /// </summary>
    protected async Task UpsertMealAsync(Guid mealId, string name, int fibreG, params (string Name, decimal Quantity, string? Unit)[] ingredients)
    {
        using HttpClient client = _factory.CreateAppClient();
        using HttpResponseMessage response = await client.PutAsJsonAsync("api/meals", new
        {
            mealId,
            name,
            slotKey = 1,
            kcal = 300,
            proteinG = 10,
            carbsG = 40,
            fibreG,
            plants = 1,
            zoeNotes = (string?)null,
            mfName = name,
            notes = (string?)null,
            ingredients = ingredients.Select(i => new { name = i.Name, quantity = i.Quantity, unit = i.Unit }),
        });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Plans a meal into the Breakfast slot on the given date through the app's own API.</summary>
    protected async Task PlanBreakfastAsync(DateOnly date, Guid mealId, decimal portionMultiplier = 1m)
    {
        using HttpClient client = _factory.CreateAppClient();
        using HttpResponseMessage response = await client.PutAsJsonAsync("api/week/entry", new
        {
            date,
            slotKey = 1,
            mealId,
            portionMultiplier,
            notes = (string?)null,
        });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Screenshots the whole page and compares it to the committed baseline named
    /// <paramref name="name"/> (see <see cref="VisualBaseline"/>). Anything that legitimately
    /// changes between runs - like today's date - must be made fixed before calling this.
    /// </summary>
    protected async Task ExpectPageToMatchBaselineAsync(string name)
    {
        byte[] png = await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            FullPage = true,
            Animations = ScreenshotAnimations.Disabled,
            Caret = ScreenshotCaret.Hide,
            Scale = ScreenshotScale.Css,
        });
        VisualBaseline.AssertMatches(name, png);
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
