using Microsoft.Playwright;
using NUnit.Framework;

namespace DietPlanner.E2ETests;

[TestFixture]
public sealed class SidebarTests : PageTestBase
{
    private ILocator Html => Page.Locator("html");
    private ILocator Sidebar => Page.Locator(".layout__sidebar");
    private ILocator SidebarLabels => Sidebar.Locator(".nav__label");

    [Test]
    public async Task Sidebar_OnDesktop_CollapsesToIconRailAndRemembersItAcrossReload()
    {
        await Page.SetViewportSizeAsync(1280, 800);
        await GoToAsync("");

        await Expect(Html).ToHaveAttributeAsync("data-sidebar", "expanded");
        await Expect(SidebarLabels.First).ToBeVisibleAsync();

        await Page.Locator(".nav__toggle").ClickAsync();

        await Expect(Html).ToHaveAttributeAsync("data-sidebar", "rail");
        await Expect(SidebarLabels.First).ToBeHiddenAsync();
        await Expect(Page.Locator(".nav__link[title='Meals']")).ToBeVisibleAsync();

        await Page.ReloadAsync();

        await Expect(Html).ToHaveAttributeAsync("data-sidebar", "rail");

        await Page.Locator(".nav__toggle").ClickAsync();

        await Expect(Html).ToHaveAttributeAsync("data-sidebar", "expanded");
        await Expect(SidebarLabels.First).ToBeVisibleAsync();
    }

    [Test]
    public async Task Sidebar_OnTablet_StartsAsIconRail()
    {
        await Page.SetViewportSizeAsync(900, 800);
        await GoToAsync("");

        await Expect(Html).ToHaveAttributeAsync("data-sidebar", "rail");
        await Expect(SidebarLabels.First).ToBeHiddenAsync();
    }

    [Test]
    public async Task Sidebar_OnMobile_StartsCollapsedAndOpensOverContentWithoutMovingIt()
    {
        await Page.SetViewportSizeAsync(375, 740);
        await GoToAsync("");

        await Expect(Sidebar).ToBeHiddenAsync();
        await Expect(Page.Locator(".layout__topbar-title")).ToHaveTextAsync("Week Plan");
        LocatorBoundingBoxResult? mainBefore = await Page.Locator(".layout__main").BoundingBoxAsync();

        await Page.Locator(".layout__topbar-toggle").ClickAsync();

        await Expect(Sidebar).ToBeVisibleAsync();
        await Expect(SidebarLabels.First).ToBeVisibleAsync();
        LocatorBoundingBoxResult? mainAfter = await Page.Locator(".layout__main").BoundingBoxAsync();
        Assert.That(mainAfter?.X, Is.EqualTo(mainBefore?.X));
        Assert.That(mainAfter?.Width, Is.EqualTo(mainBefore?.Width));
    }

    [Test]
    public async Task Sidebar_OnMobile_ClosesOnBackdropTapAndEscape()
    {
        await Page.SetViewportSizeAsync(375, 740);
        await GoToAsync("");

        await Page.Locator(".layout__topbar-toggle").ClickAsync();
        await Expect(Sidebar).ToBeVisibleAsync();

        // Tap the backdrop to the right of the drawer, not in its centre where the drawer sits on top.
        await Page.Locator(".layout__backdrop").ClickAsync(new LocatorClickOptions { Position = new Position { X = 360, Y = 400 } });
        await Expect(Sidebar).ToBeHiddenAsync();

        await Page.Locator(".layout__topbar-toggle").ClickAsync();
        await Expect(Sidebar).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Expect(Sidebar).ToBeHiddenAsync();
    }

    [Test]
    public async Task Sidebar_OnMobile_ClosesAfterChoosingANavLink()
    {
        await Page.SetViewportSizeAsync(375, 740);
        await GoToAsync("");

        await Page.Locator(".layout__topbar-toggle").ClickAsync();
        await Page.Locator(".nav__link[title='Meals']").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/meals$"));
        await Expect(Sidebar).ToBeHiddenAsync();
        await Expect(Page.Locator(".layout__topbar-title")).ToHaveTextAsync("Meals");
    }

    [Test]
    public async Task MealsTable_OnMobile_RendersEachMealAsALabelledCard()
    {
        await UpsertMealAsync(Guid.NewGuid(), "Porridge", fibreG: 6);
        await Page.SetViewportSizeAsync(375, 740);
        await GoToAsync("meals");

        await Expect(Page.Locator("table.table thead")).ToBeHiddenAsync();
        await Expect(Page.Locator("table.table td[data-label='Name']")).ToHaveTextAsync("Porridge");
        await Expect(Page.Locator("table.table td[data-label='Kcal']")).ToHaveTextAsync("300");
        Assert.That(await Page.EvaluateAsync<int>("document.documentElement.scrollWidth"), Is.LessThanOrEqualTo(375));
    }
}
