using Microsoft.Playwright;
using NUnit.Framework;

namespace DietPlanner.E2ETests;

[TestFixture]
public sealed class SettingsPageTests : PageTestBase
{
    [Test]
    public async Task SettingsPage_LoadsWithDefaultTargets()
    {
        await GoToAsync("settings");

        await Expect(Page.Locator("#target-kcal")).ToBeVisibleAsync();
        await Expect(Page.Locator("#target-protein")).ToBeVisibleAsync();
        await Expect(Page.Locator("#target-carbs")).ToBeVisibleAsync();
        await Expect(Page.Locator("#target-fibre")).ToBeVisibleAsync();
        await Expect(Page.Locator("#target-plants")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SettingsPage_SavingValidTargets_ShowsSuccessMessage()
    {
        await GoToAsync("settings");

        await Page.Locator("#target-kcal").FillAsync("2200");
        await Page.Locator("#target-protein").FillAsync("160");
        await Page.Locator("#target-carbs").FillAsync("210");
        await Page.Locator("#target-fibre").FillAsync("28");
        await Page.Locator("#target-plants").FillAsync("30");
        await Page.Locator("button.btn--primary").ClickAsync();

        await Expect(Page.Locator(".alert--success")).ToContainTextAsync("Daily targets saved.");
    }

    [Test]
    public async Task SettingsPage_SavingOutOfRangeKcal_ShowsValidationError()
    {
        await GoToAsync("settings");

        await Page.Locator("#target-kcal").FillAsync("100");
        await Page.Locator("button.btn--primary").ClickAsync();

        await Expect(Page.Locator(".form__errors")).ToBeVisibleAsync();
    }
}
