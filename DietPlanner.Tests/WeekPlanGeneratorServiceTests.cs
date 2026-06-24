using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Settings;
using DietPlanner.Endpoints.Slots;
using DietPlanner.Endpoints.WeekPlan;
using FluentAssertions;
using Xunit;

namespace DietPlanner.Tests;

public sealed class WeekPlanGeneratorServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();

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

    [Fact]
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

        result.Should().Be(GenerateWeekPlanResult.NoMealsAvailableForSlot);
        missingSlot.Should().Be(SlotKey.Lunch);
        entries.Should().BeEmpty();
    }

    [Fact]
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

        result.Should().Be(GenerateWeekPlanResult.Success);
        missingSlot.Should().BeNull();
        entries.Should().HaveCount(7 * Enum.GetValues<SlotKey>().Length);
        entries.Select(e => e.Date).Distinct().Should().HaveCount(7);
        foreach (DateOnly date in Enumerable.Range(0, 7).Select(weekStart.AddDays))
        {
            entries.Where(e => e.Date == date).Select(e => e.SlotKey).Should()
                .BeEquivalentTo(Enum.GetValues<SlotKey>());
        }
    }

    [Fact]
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

        stored.Should().HaveCount(secondEntries.Count);
    }

    public void Dispose() => _database.Dispose();
}
