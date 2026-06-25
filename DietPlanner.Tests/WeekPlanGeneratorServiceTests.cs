using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Settings;
using DietPlanner.Endpoints.Slots;
using DietPlanner.Endpoints.WeekPlan;

namespace DietPlanner.Tests;

[TestFixture]
public sealed class WeekPlanGeneratorServiceTests
{
    private TestDatabase _database = null!;

    [SetUp]
    public void SetUp() => _database = new TestDatabase();

    [TearDown]
    public void TearDown() => _database.Dispose();

    private static MealEntry NewMeal(SlotKey slot, string name, int carbs = 40, int protein = 20) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        SlotKey = slot,
        Kcal = 300,
        ProteinG = protein,
        CarbsG = carbs,
        FibreG = 5,
        Plants = 2,
        MfName = name + " MFP"
    };

    private async Task SeedSlotsAsync(AppDbContext db)
    {
        foreach (SlotKey key in Enum.GetValues<SlotKey>())
        {
            db.Slots.Add(new Slot(key, key.ToString(), (int)key));
        }
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task GenerateWeekAsync_WithNoMealsForASlot_ReturnsNoMealsAvailableForSlot()
    {
        using AppDbContext db = _database.CreateContext();
        await SeedSlotsAsync(db);
        db.Meals.Add(NewMeal(SlotKey.Breakfast, "Oatmeal"));
        await db.SaveChangesAsync();

        WeekPlanGeneratorService service = new(db, new SettingsService(db));

        var (result, missingSlot, entries) = await service.GenerateWeekAsync(
            new GenerateWeekPlanRequest(new DateOnly(2026, 1, 5)),
            CancellationToken.None);

        Assert.That(result, Is.EqualTo(GenerateWeekPlanResult.NoMealsAvailableForSlot));
        Assert.That(missingSlot, Is.EqualTo(SlotKey.Lunch));
        Assert.That(entries, Is.Empty);
    }

    [Test]
    public async Task GenerateWeekAsync_WithMealsForEverySlot_FillsAllSevenDaysAndEverySlot()
    {
        using AppDbContext db = _database.CreateContext();
        await SeedSlotsAsync(db);
        foreach (SlotKey key in Enum.GetValues<SlotKey>())
        {
            db.Meals.Add(NewMeal(key, $"{key} meal"));
        }
        await db.SaveChangesAsync();

        WeekPlanGeneratorService service = new(db, new SettingsService(db));
        DateOnly weekStart = new(2026, 1, 5);

        var (result, missingSlot, entries) = await service.GenerateWeekAsync(
            new GenerateWeekPlanRequest(weekStart),
            CancellationToken.None);

        Assert.That(result, Is.EqualTo(GenerateWeekPlanResult.Success));
        Assert.That(missingSlot, Is.Null);
        Assert.That(entries, Has.Count.EqualTo(7 * Enum.GetValues<SlotKey>().Length));
        Assert.That(entries.Select(e => e.Date).Distinct().Count(), Is.EqualTo(7));
        foreach (DateOnly date in Enumerable.Range(0, 7).Select(weekStart.AddDays))
        {
            Assert.That(
                entries.Where(e => e.Date == date).Select(e => e.SlotKey),
                Is.EquivalentTo(Enum.GetValues<SlotKey>()));
        }
    }

    [Test]
    public async Task GenerateWeekAsync_CalledTwiceForSameWeek_ReplacesPreviousEntries()
    {
        using AppDbContext db = _database.CreateContext();
        await SeedSlotsAsync(db);
        foreach (SlotKey key in Enum.GetValues<SlotKey>())
        {
            db.Meals.Add(NewMeal(key, $"{key} meal"));
        }
        await db.SaveChangesAsync();

        WeekPlanGeneratorService service = new(db, new SettingsService(db));
        DateOnly weekStart = new(2026, 1, 5);

        await service.GenerateWeekAsync(new GenerateWeekPlanRequest(weekStart), CancellationToken.None);
        var (_, _, secondEntries) = await service.GenerateWeekAsync(new GenerateWeekPlanRequest(weekStart), CancellationToken.None);

        WeekPlanService weekPlanService = new(db);
        IReadOnlyList<WeekPlanEntryDto> stored = await weekPlanService.GetWeekAsync(weekStart, weekStart.AddDays(6), CancellationToken.None);

        Assert.That(stored, Has.Count.EqualTo(secondEntries.Count));
    }

}
