using Microsoft.Playwright;
using NUnit.Framework;

namespace DietPlanner.E2ETests;

[TestFixture]
public sealed class WeekPlanPageTests : PageTestBase
{
    [Test]
    public async Task WeekPlanPage_LoadsWeekHeading()
    {
        await GoToAsync("");

        await Expect(Page.Locator("h1")).ToContainTextAsync("Week Plan");
    }

    [Test]
    public async Task WeekPlanPage_GeneratingWithNoMealsUploaded_ShowsErrorMessage()
    {
        await GoToAsync("");

        await Page.Locator("button.btn--primary").ClickAsync();

        await Expect(Page.Locator(".alert--error")).ToContainTextAsync("Cannot generate this week");
    }

    [Test]
    public async Task WeekPlanPage_NavigatingToNextWeek_UpdatesHeading()
    {
        await GoToAsync("");

        string initialHeading = await Page.Locator("p strong").First.InnerTextAsync();
        await Page.GetByText("Next week").ClickAsync();

        await Expect(Page.Locator("p strong").First).Not.ToHaveTextAsync(initialHeading);
    }
}
