using DietPlanner.Endpoints.DayPlan;
using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Settings;
using DietPlanner.Endpoints.Slots;
using DietPlanner.Endpoints.WeekPlan;

namespace DietPlanner.Tests;

[TestFixture]
public sealed class DayPlanServiceTests
{
    private TestDatabase _database = null!;

    [SetUp]
    public void SetUp() => _database = new TestDatabase();

    [TearDown]
    public void TearDown() => _database.Dispose();

    [Test]
    public async Task GetDayPlanAsync_WithNoEntries_ReturnsZeroTotalsAndDefaultTargetsAsRemaining()
    {
        using AppDbContext db = _database.CreateContext();
        DayPlanService service = new(db, new SettingsService(db));
        DateOnly date = new(2026, 1, 5);

        DayPlanResponseDto result = await service.GetDayPlanAsync(date, CancellationToken.None);

        Assert.That(result.Date, Is.EqualTo(date));
        Assert.That(result.Meals, Is.Empty);
        Assert.That(result.Totals, Is.EqualTo(new DayPlanTotalsDto(0, 0, 0, 0, 0)));
        Assert.That(result.Remaining, Is.EqualTo(new DayPlanTotalsDto(2300, 165, 220, 30, 30)));
    }

    [Test]
    public async Task GetDayPlanAsync_ScalesIngredientsAndMacrosByPortionMultiplier()
    {
        Guid mealId = Guid.NewGuid();
        DateOnly date = new(2026, 1, 5);

        using (AppDbContext db = _database.CreateContext())
        {
            db.Slots.Add(new Slot(SlotKey.Breakfast, "Breakfast", 1));
            db.Meals.Add(new MealEntry
            {
                Id = mealId,
                Name = "Oatmeal",
                SlotKey = SlotKey.Breakfast,
                Kcal = 200,
                ProteinG = 10,
                CarbsG = 30,
                FibreG = 4,
                Plants = 1,
                MfName = "Oatmeal MFP",
                Ingredients = new List<MealIngredient>
                {
                    new() { Id = Guid.NewGuid(), MealId = mealId, Name = "Oats", Quantity = 50, Unit = "g" }
                }
            });
            db.WeekPlanEntries.Add(new WeekPlanEntry(Guid.NewGuid(), date, SlotKey.Breakfast, mealId, 2m, "double portion"));
            await db.SaveChangesAsync();
        }

        using AppDbContext readDb = _database.CreateContext();
        DayPlanService service = new(readDb, new SettingsService(readDb));

        DayPlanResponseDto result = await service.GetDayPlanAsync(date, CancellationToken.None);

        Assert.That(result.Meals, Has.Count.EqualTo(1));
        DayPlanMealDto meal = result.Meals.Single();
        Assert.That(meal.Kcal, Is.EqualTo(400));
        Assert.That(meal.ProteinG, Is.EqualTo(20));
        Assert.That(meal.CarbsG, Is.EqualTo(60));
        Assert.That(meal.Ingredients.Count(i => i.Name == "Oats" && i.Quantity == 100), Is.EqualTo(1));

        Assert.That(result.Totals, Is.EqualTo(new DayPlanTotalsDto(400, 20, 60, 8, 2)));
        Assert.That(result.Remaining, Is.EqualTo(new DayPlanTotalsDto(2300 - 400, 165 - 20, 220 - 60, 30 - 8, 30 - 2)));
    }

    [Test]
    public async Task GetDayPlanAsync_OrdersMealsBySlotSortOrder()
    {
        Guid breakfastMealId = Guid.NewGuid();
        Guid dinnerMealId = Guid.NewGuid();
        DateOnly date = new(2026, 1, 5);

        using (AppDbContext db = _database.CreateContext())
        {
            db.Slots.Add(new Slot(SlotKey.Breakfast, "Breakfast", 1));
            db.Slots.Add(new Slot(SlotKey.Dinner, "Dinner", 3));
            db.Meals.Add(new MealEntry { Id = breakfastMealId, Name = "Oatmeal", SlotKey = SlotKey.Breakfast, Kcal = 200, ProteinG = 10, CarbsG = 30, FibreG = 4, Plants = 1, MfName = "Oatmeal MFP" });
            db.Meals.Add(new MealEntry { Id = dinnerMealId, Name = "Stew", SlotKey = SlotKey.Dinner, Kcal = 500, ProteinG = 30, CarbsG = 40, FibreG = 8, Plants = 4, MfName = "Stew MFP" });
            db.WeekPlanEntries.Add(new WeekPlanEntry(Guid.NewGuid(), date, SlotKey.Dinner, dinnerMealId, 1m, null));
            db.WeekPlanEntries.Add(new WeekPlanEntry(Guid.NewGuid(), date, SlotKey.Breakfast, breakfastMealId, 1m, null));
            await db.SaveChangesAsync();
        }

        using AppDbContext readDb = _database.CreateContext();
        DayPlanService service = new(readDb, new SettingsService(readDb));

        DayPlanResponseDto result = await service.GetDayPlanAsync(date, CancellationToken.None);

        Assert.That(result.Meals.Select(m => m.SlotKey), Is.EqualTo(new[] { SlotKey.Breakfast, SlotKey.Dinner }));
    }

}
