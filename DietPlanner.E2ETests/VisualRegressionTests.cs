using Microsoft.Playwright;
using NUnit.Framework;

namespace DietPlanner.E2ETests;

/// <summary>
/// Full-page screenshots of each page compared against the baselines in VisualBaselines/. These
/// catch layout and styling regressions the behavioural tests can't see; when a change is intended,
/// regenerate the baselines and let the reviewer approve the new images in the pull request.
/// </summary>
[TestFixture]
[Category("Visual")]
public sealed class VisualRegressionTests : PageTestBase
{
    private const int DesktopWidth = 1280;
    private const int DesktopHeight = 800;
    private const int MobileWidth = 375;
    private const int MobileHeight = 740;

    public override BrowserNewContextOptions ContextOptions()
    {
        BrowserNewContextOptions options = base.ContextOptions();
        // Pinned so a runner's defaults (OS colour scheme, HiDPI, locale) can't change the rendering.
        options.ViewportSize = new ViewportSize { Width = DesktopWidth, Height = DesktopHeight };
        options.DeviceScaleFactor = 1;
        options.ColorScheme = ColorScheme.Light;
        options.Locale = "en-GB";
        return options;
    }

    [Test]
    public async Task WeekPlan_Desktop()
    {
        await GoToAsync("");
        await Expect(Page.Locator(".week-plan__day").First).ToBeVisibleAsync();

        await FreezeWeekDatesAsync();
        await ExpectPageToMatchBaselineAsync("week-plan.desktop");
    }

    [Test]
    public async Task WeekPlan_ShoppingList_Desktop()
    {
        await GoToAsync("");
        await Page.Locator(".week-plan__tab").GetByText("Shopping list").ClickAsync();
        await Expect(Page.Locator(".week-plan__tab--active")).ToContainTextAsync("Shopping list");

        await FreezeWeekDatesAsync();
        await ExpectPageToMatchBaselineAsync("week-plan-shopping-list.desktop");
    }

    [Test]
    public async Task Meals_Desktop()
    {
        await SeedMealsAsync();
        await GoToAsync("meals");
        await Expect(Page.Locator("table.table")).ToContainTextAsync("Porridge");

        await ExpectPageToMatchBaselineAsync("meals.desktop");
    }

    [Test]
    public async Task Meals_AddMealForm_Desktop()
    {
        await GoToAsync("meals");
        await Page.Locator("button.btn--primary").GetByText("Add a new meal").ClickAsync();
        await Expect(Page.Locator(".meal-form")).ToBeVisibleAsync();

        await ExpectPageToMatchBaselineAsync("meals-add-form.desktop");
    }

    [Test]
    public async Task Meals_DarkMode_Desktop()
    {
        await SeedMealsAsync();
        await GoToAsync("meals");
        await Page.Locator(".nav__theme-toggle").ClickAsync();
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");

        await ExpectPageToMatchBaselineAsync("meals-dark.desktop");
    }

    [Test]
    public async Task Meals_CollapsedSidebar_Desktop()
    {
        await SeedMealsAsync();
        await GoToAsync("meals");
        await Page.Locator(".nav__toggle").ClickAsync();
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-sidebar", "rail");

        await ExpectPageToMatchBaselineAsync("meals-sidebar-rail.desktop");
    }

    [Test]
    public async Task MealUpload_Desktop()
    {
        await GoToAsync("meals/upload");
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Upload Meals");

        await ExpectPageToMatchBaselineAsync("meal-upload.desktop");
    }

    [Test]
    public async Task Settings_Desktop()
    {
        await GoToAsync("settings");
        await Expect(Page.Locator("#target-kcal")).ToBeVisibleAsync();

        await ExpectPageToMatchBaselineAsync("settings.desktop");
    }

    [Test]
    public async Task Meals_Mobile()
    {
        await SeedMealsAsync();
        await Page.SetViewportSizeAsync(MobileWidth, MobileHeight);
        await GoToAsync("meals");
        await Expect(Page.Locator("table.table")).ToContainTextAsync("Porridge");

        await ExpectPageToMatchBaselineAsync("meals.mobile");
    }

    [Test]
    public async Task WeekPlan_SidebarOpen_Mobile()
    {
        await Page.SetViewportSizeAsync(MobileWidth, MobileHeight);
        await GoToAsync("");
        await Page.Locator(".layout__topbar-toggle").ClickAsync();
        await Expect(Page.Locator(".layout__sidebar")).ToBeVisibleAsync();

        await FreezeWeekDatesAsync();
        await ExpectPageToMatchBaselineAsync("week-plan-sidebar-open.mobile");
    }

    // The week plan always shows the current week, so its dates change from run to run. Swapping in
    // fixed text (rather than masking) also stops a longer date re-wrapping the intro on mobile.
    private Task FreezeWeekDatesAsync() =>
        Page.EvaluateAsync("""
            () => {
                document.querySelectorAll('.layout__main p strong').forEach(e => e.textContent = 'Monday 1 January 2024');
                document.querySelectorAll('.week-plan__day h2').forEach(e => e.textContent = 'Monday 1 January');
            }
            """);

    private async Task SeedMealsAsync()
    {
        await UpsertMealAsync(Guid.NewGuid(), "Porridge", fibreG: 6, ("Oats", 80m, "g"), ("Milk", 200m, "ml"));
        await UpsertMealAsync(Guid.NewGuid(), "Greek yoghurt with berries", fibreG: 4, ("Greek yoghurt", 150m, "g"), ("Blueberries", 80m, "g"));
        await UpsertMealAsync(Guid.NewGuid(), "Scrambled eggs on toast", fibreG: 3, ("Egg", 2m, null), ("Sourdough", 1m, "slice"));
    }
}
