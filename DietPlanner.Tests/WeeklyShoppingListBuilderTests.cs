using DietPlanner.Endpoints.DayPlan;
using DietPlanner.Endpoints.Settings;
using DietPlanner.Endpoints.Slots;

namespace DietPlanner.Tests;

[TestFixture]
public sealed class WeeklyShoppingListBuilderTests
{
    private static readonly DayPlanTotalsDto NoTotals = new(0, 0, 0, 0, 0);

    private static DayPlanMealDto Meal(
        IReadOnlyList<DayPlanIngredientDto> ingredients, IReadOnlyList<SuggestedMealAddition>? suggestedAdditions)
        => new(new DateOnly(2026, 1, 5), SlotKey.Breakfast, 1, Guid.NewGuid(), "Porridge", "Porridge MFP", 1m,
            300, 10, 40, 5, 2, null, null, ingredients, suggestedAdditions);

    private static DayPlanResponseDto Day(params DayPlanMealDto[] meals)
        => new(new DateOnly(2026, 1, 5), meals, NoTotals, new SettingsDto(2300, 165, 220, 30, 30), NoTotals);

    [Test]
    public void Build_IncludesSuggestedAdditionsAlongsideMealIngredients()
    {
        DayPlanResponseDto day = Day(Meal(
            [new DayPlanIngredientDto("Oats", 50m, "g")],
            [new SuggestedMealAddition("Chia seeds", 1m, "tbsp", "Adds fibre.")]));

        List<DayPlanIngredientDto> result = WeeklyShoppingListBuilder.Build([day]);

        Assert.That(result, Is.EqualTo(new[]
        {
            new DayPlanIngredientDto("Chia seeds", 1m, "tbsp"),
            new DayPlanIngredientDto("Oats", 50m, "g"),
        }));
    }

    [Test]
    public void Build_SumsSuggestedAdditionsWithMatchingIngredientsAcrossDays()
    {
        DayPlanResponseDto monday = Day(Meal(
            [new DayPlanIngredientDto("Chia seeds", 1m, "tbsp")],
            [new SuggestedMealAddition("Chia seeds", 2m, "tbsp", "Adds fibre.")]));
        DayPlanResponseDto tuesday = Day(Meal(
            [],
            [new SuggestedMealAddition("Chia seeds", 1.5m, "tbsp", "Adds fibre.")]));

        List<DayPlanIngredientDto> result = WeeklyShoppingListBuilder.Build([monday, tuesday]);

        Assert.That(result, Is.EqualTo(new[] { new DayPlanIngredientDto("Chia seeds", 4.5m, "tbsp") }));
    }

    [Test]
    public void Build_WithNoSuggestionsGenerated_ReturnsOnlyMealIngredients()
    {
        DayPlanResponseDto day = Day(Meal([new DayPlanIngredientDto("Oats", 50m, "g")], null));

        List<DayPlanIngredientDto> result = WeeklyShoppingListBuilder.Build([day]);

        Assert.That(result, Is.EqualTo(new[] { new DayPlanIngredientDto("Oats", 50m, "g") }));
    }

    [Test]
    public void Build_KeepsSameNameWithDifferentUnitsSeparate()
    {
        DayPlanResponseDto day = Day(Meal(
            [new DayPlanIngredientDto("Chia seeds", 10m, "g")],
            [new SuggestedMealAddition("Chia seeds", 1m, "tbsp", "Adds fibre.")]));

        List<DayPlanIngredientDto> result = WeeklyShoppingListBuilder.Build([day]);

        Assert.That(result, Has.Count.EqualTo(2));
    }
}
