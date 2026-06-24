using Microsoft.Playwright;
using NUnit.Framework;

namespace DietPlanner.E2ETests;

[TestFixture]
public sealed class DarkModeTests : PageTestBase
{
    [Test]
    public async Task ThemeToggle_Clicking_SwitchesToDarkModeAndPersistsAcrossReload()
    {
        await GoToAsync("");

        ILocator toggle = Page.Locator(".nav__theme-toggle");
        await Expect(toggle).ToContainTextAsync("Dark mode");

        await toggle.ClickAsync();

        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");
        await Expect(toggle).ToContainTextAsync("Light mode");

        await Page.ReloadAsync();

        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");
    }
}
