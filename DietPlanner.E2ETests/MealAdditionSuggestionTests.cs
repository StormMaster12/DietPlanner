using System.Text.Json;
using Microsoft.Playwright;

namespace DietPlanner.E2ETests;

/// <summary>
/// Covers AI-suggested meal additions on the week plan page: they're saved against the meal, shown
/// again after a reload without another Anthropic call, reused for the same meal on other days,
/// regenerated once the meal changes, and included in the shopping list.
/// </summary>
[TestFixture]
public sealed class MealAdditionSuggestionTests : PageTestBase
{
    private const string ChiaSeedsSuggestion =
        """[{"ingredient":"Chia seeds","quantity":1,"unit":"tbsp","reason":"Adds fibre without changing the flavour."}]""";

    private const string FlaxseedSuggestion =
        """[{"ingredient":"Ground flaxseed","quantity":2,"unit":"tbsp","reason":"Adds fibre to the batter."}]""";

    /// <summary>The Monday of the week the page shows on first load (the container's clock is UTC).</summary>
    private static DateOnly CurrentWeekMonday
    {
        get
        {
            DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
            return today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        }
    }

    private static string BuildAnthropicResponse(string text)
        => JsonSerializer.Serialize(new { content = new[] { new { type = "text", text } } });

    private ILocator SuggestionLists => Page.Locator(".meal-suggestions__list");

    private async Task<Guid> SeedPorridgeOnMondayAsync()
    {
        Guid mealId = Guid.NewGuid();
        await UpsertMealAsync(mealId, "Porridge", fibreG: 5, ("Oats", 50m, "g"));
        await PlanBreakfastAsync(CurrentWeekMonday, mealId);
        return mealId;
    }

    /// <summary>Clicks the first day's suggest button and waits until the page reports that run has finished.</summary>
    private async Task SuggestAdditionsForFirstDayAsync()
    {
        ILocator toolbar = Page.Locator(".week-plan__toolbar");
        int completedRuns = int.Parse(await toolbar.GetAttributeAsync("data-completed-suggestion-runs") ?? "0");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Suggest additions for all meals" }).First.ClickAsync();

        await Expect(toolbar).ToHaveAttributeAsync("data-completed-suggestion-runs", (completedRuns + 1).ToString());
    }

    [Test]
    public async Task GeneratedSuggestions_AreShownUnderTheMeal()
    {
        await SeedPorridgeOnMondayAsync();
        await StubAnthropicResponseAsync(BuildAnthropicResponse(ChiaSeedsSuggestion));
        await GoToAsync("");

        await SuggestAdditionsForFirstDayAsync();

        await Expect(SuggestionLists.First).ToContainTextAsync("1 tbsp Chia seeds");
        await Expect(SuggestionLists.First).ToContainTextAsync("Adds fibre without changing the flavour.");
    }

    [Test]
    public async Task GeneratedSuggestions_AreStillShownAfterReload_WithoutCallingAnthropicAgain()
    {
        await SeedPorridgeOnMondayAsync();
        await StubAnthropicResponseAsync(BuildAnthropicResponse(ChiaSeedsSuggestion));
        await GoToAsync("");
        await SuggestAdditionsForFirstDayAsync();
        await Expect(SuggestionLists.First).ToContainTextAsync("Chia seeds");

        await GoToAsync("");

        await Expect(SuggestionLists.First).ToContainTextAsync("1 tbsp Chia seeds");
        Assert.That(await GetAnthropicRequestBodiesAsync(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SuggestingAgainForAnUnchangedMeal_ReusesSavedSuggestions()
    {
        await SeedPorridgeOnMondayAsync();
        await StubAnthropicResponseAsync(BuildAnthropicResponse(ChiaSeedsSuggestion));
        await GoToAsync("");
        await SuggestAdditionsForFirstDayAsync();
        await Expect(SuggestionLists.First).ToContainTextAsync("Chia seeds");

        await GoToAsync("");
        await SuggestAdditionsForFirstDayAsync();

        await Expect(SuggestionLists.First).ToContainTextAsync("1 tbsp Chia seeds");
        Assert.That(await GetAnthropicRequestBodiesAsync(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SuggestionsForAMeal_AreShownOnEveryDayThatMealIsPlanned()
    {
        Guid mealId = await SeedPorridgeOnMondayAsync();
        await PlanBreakfastAsync(CurrentWeekMonday.AddDays(1), mealId, portionMultiplier: 2m);
        await StubAnthropicResponseAsync(BuildAnthropicResponse(ChiaSeedsSuggestion));
        await GoToAsync("");

        await SuggestAdditionsForFirstDayAsync();

        await Expect(SuggestionLists).ToHaveCountAsync(2);
        await Expect(SuggestionLists.Nth(0)).ToContainTextAsync("1 tbsp Chia seeds");
        await Expect(SuggestionLists.Nth(1)).ToContainTextAsync("2 tbsp Chia seeds");
        Assert.That(await GetAnthropicRequestBodiesAsync(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task EditingTheMeal_HidesOldSuggestionsAndRegeneratingReplacesThem()
    {
        Guid mealId = await SeedPorridgeOnMondayAsync();
        await StubAnthropicResponseAsync(BuildAnthropicResponse(ChiaSeedsSuggestion));
        await GoToAsync("");
        await SuggestAdditionsForFirstDayAsync();
        await Expect(SuggestionLists.First).ToContainTextAsync("Chia seeds");

        await UpsertMealAsync(mealId, "Porridge", fibreG: 5, ("Oats", 50m, "g"), ("Banana", 1m, null));
        await ClearAnthropicStubsAsync();
        await StubAnthropicResponseAsync(BuildAnthropicResponse(FlaxseedSuggestion));
        await GoToAsync("");

        await Expect(SuggestionLists).ToHaveCountAsync(0);

        await SuggestAdditionsForFirstDayAsync();

        await Expect(SuggestionLists.First).ToContainTextAsync("2 tbsp Ground flaxseed");
        await Expect(SuggestionLists.First).Not.ToContainTextAsync("Chia seeds");
        Assert.That(await GetAnthropicRequestBodiesAsync(), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task SavedSuggestions_AreIncludedInTheShoppingList()
    {
        await SeedPorridgeOnMondayAsync();
        await StubAnthropicResponseAsync(BuildAnthropicResponse(ChiaSeedsSuggestion));
        await GoToAsync("");
        await SuggestAdditionsForFirstDayAsync();
        await Expect(SuggestionLists.First).ToContainTextAsync("Chia seeds");

        // Reload first so the shopping list is built purely from what was saved, not page state.
        await GoToAsync("");
        await ClearAnthropicStubsAsync();
        await StubAnthropicResponseAsync(BuildAnthropicResponse(
            """[{"index":0,"grams":12,"category":"Pantry & Dry Goods"},{"index":1,"grams":50,"category":"Pantry & Dry Goods"}]"""));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Shopping list" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Group by aisle" }).ClickAsync();

        await Expect(Page.Locator(".shopping-list__item")).ToHaveTextAsync(["12 g Chia seeds", "50 g Oats"]);
        IReadOnlyList<string> requests = await GetAnthropicRequestBodiesAsync();
        Assert.That(requests, Has.Count.EqualTo(2));
        string groupingRequest = requests.Single(r => r.Contains("supermarket aisle"));
        Assert.That(groupingRequest, Does.Contain("Chia seeds"));
    }
}
