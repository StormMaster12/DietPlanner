using Microsoft.Playwright;
using NUnit.Framework;

namespace DietPlanner.E2ETests;

[TestFixture]
public sealed class MealsPageTests : PageTestBase
{
    [Test]
    public async Task MealsPage_WithNoMeals_ShowsEmptyState()
    {
        await GoToAsync("meals");

        await Expect(Page.GetByText("No meals have been added yet.")).ToBeVisibleAsync();
    }

    [Test]
    public async Task MealsPage_AddingNewMeal_AppearsInTable()
    {
        await GoToAsync("meals");

        await Page.Locator("button.btn--primary").GetByText("Add a new meal").ClickAsync();
        await Page.Locator("#meal-name").FillAsync("Oatmeal");
        await Page.Locator("#meal-kcal").FillAsync("300");
        await Page.Locator("#meal-protein").FillAsync("10");
        await Page.Locator("#meal-carbs").FillAsync("40");
        await Page.Locator("#meal-fibre").FillAsync("5");
        await Page.Locator("#meal-plants").FillAsync("2");
        await Page.Locator("#meal-mfname").FillAsync("Oatmeal MFP");

        await Page.Locator("button.btn--secondary").GetByText("Add ingredient").ClickAsync();
        await Page.Locator(".ingredient-table__input[placeholder='e.g. Chicken breast']").FillAsync("Oats");
        await Page.Locator(".ingredient-table input[type='number']").First.FillAsync("80");
        await Page.Locator(".ingredient-table__input[placeholder='e.g. g']").FillAsync("g");

        await Page.Locator("button.btn--primary").GetByText("Save meal").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.Locator("table.table")).ToContainTextAsync("Oatmeal");
    }

    [Test]
    public async Task MealsPage_EditingExistingMeal_UpdatesTable()
    {
        await GoToAsync("meals");
        await AddOatmealMealAsync();

        await Page.Locator("button.btn--secondary").GetByText("Edit").ClickAsync();
        await Page.Locator("#meal-kcal").FillAsync("350");
        await Page.Locator("button.btn--primary").GetByText("Save meal").ClickAsync();

        await Expect(Page.Locator("table.table")).ToContainTextAsync("350");
    }

    [Test]
    public async Task MealsPage_DeletingMeal_RemovesItFromTable()
    {
        await GoToAsync("meals");
        await AddOatmealMealAsync();

        await Page.Locator("button.btn--danger").GetByText("Delete").ClickAsync();

        await Expect(Page.GetByText("No meals have been added yet.")).ToBeVisibleAsync();
    }

    private async Task AddOatmealMealAsync()
    {
        await Page.Locator("button.btn--primary").GetByText("Add a new meal").ClickAsync();
        await Page.Locator("#meal-name").FillAsync("Oatmeal");
        await Page.Locator("#meal-kcal").FillAsync("300");
        await Page.Locator("#meal-protein").FillAsync("10");
        await Page.Locator("#meal-carbs").FillAsync("40");
        await Page.Locator("#meal-fibre").FillAsync("5");
        await Page.Locator("#meal-plants").FillAsync("2");
        await Page.Locator("#meal-mfname").FillAsync("Oatmeal MFP");
        await Page.Locator("button.btn--primary").GetByText("Save meal").ClickAsync();
    }
}
