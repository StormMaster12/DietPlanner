using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Slots;
using DietPlanner.Endpoints.WeekPlan;
using FluentAssertions;
using Xunit;

namespace DietPlanner.Tests;

public sealed class WeekPlanServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly Guid _mealId = Guid.NewGuid();

    private async Task SeedSlotAndMealAsync()
    {
        using AppDbContext db = _database.CreateContext();
        db.Slots.Add(new Slot(SlotKey.Breakfast, "Breakfast", 1));
        db.Meals.Add(new MealEntry
        {
            Id = _mealId,
            Name = "Oatmeal",
            SlotKey = SlotKey.Breakfast,
            Kcal = 300,
            ProteinG = 10,
            CarbsG = 40,
            FibreG = 5,
            Plants = 2,
            MfName = "Oatmeal MFP"
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task UpsertEntryAsync_WithUnknownSlot_ReturnsInvalidSlot()
    {
        await SeedSlotAndMealAsync();
        using AppDbContext db = _database.CreateContext();
        WeekPlanService service = new(db);

        var (result, entry) = await service.UpsertEntryAsync(
            new UpsertWeekEntryRequest(new DateOnly(2026, 1, 5), SlotKey.Lunch, _mealId, 1m, null),
            CancellationToken.None);

        result.Should().Be(UpsertWeekEntryResult.InvalidSlot);
        entry.Should().BeNull();
    }

    [Fact]
    public async Task UpsertEntryAsync_WithUnknownMeal_ReturnsUnknownMeal()
    {
        await SeedSlotAndMealAsync();
        using AppDbContext db = _database.CreateContext();
        WeekPlanService service = new(db);

        var (result, entry) = await service.UpsertEntryAsync(
            new UpsertWeekEntryRequest(new DateOnly(2026, 1, 5), SlotKey.Breakfast, Guid.NewGuid(), 1m, null),
            CancellationToken.None);

        result.Should().Be(UpsertWeekEntryResult.UnknownMeal);
        entry.Should().BeNull();
    }

    [Fact]
    public async Task UpsertEntryAsync_WithNewEntry_CreatesEntry()
    {
        await SeedSlotAndMealAsync();
        using AppDbContext db = _database.CreateContext();
        WeekPlanService service = new(db);
        DateOnly date = new(2026, 1, 5);

        var (result, entry) = await service.UpsertEntryAsync(
            new UpsertWeekEntryRequest(date, SlotKey.Breakfast, _mealId, 1.5m, "extra hungry"),
            CancellationToken.None);

        result.Should().Be(UpsertWeekEntryResult.Success);
        entry.Should().Be(new WeekPlanEntryDto(date, SlotKey.Breakfast, _mealId, 1.5m, "extra hungry"));
    }

    [Fact]
    public async Task UpsertEntryAsync_CalledTwiceForSameSlot_UpdatesExistingEntry()
    {
        // Regression test: the previous implementation reassigned a `record with` expression to a
        // local variable instead of attaching it to the change tracker, so the update was silently
        // dropped and the entry kept its original PortionMultiplier/Notes.
        await SeedSlotAndMealAsync();
        DateOnly date = new(2026, 1, 5);

        using (AppDbContext db1 = _database.CreateContext())
        {
            await new WeekPlanService(db1).UpsertEntryAsync(
                new UpsertWeekEntryRequest(date, SlotKey.Breakfast, _mealId, 1m, "first"),
                CancellationToken.None);
        }

        using AppDbContext db2 = _database.CreateContext();
        WeekPlanService service = new(db2);

        var (result, entry) = await service.UpsertEntryAsync(
            new UpsertWeekEntryRequest(date, SlotKey.Breakfast, _mealId, 2m, "second"),
            CancellationToken.None);

        result.Should().Be(UpsertWeekEntryResult.Success);
        entry.Should().Be(new WeekPlanEntryDto(date, SlotKey.Breakfast, _mealId, 2m, "second"));

        IReadOnlyList<WeekPlanEntryDto> all = await service.GetWeekAsync(date, date, CancellationToken.None);
        all.Should().ContainSingle().Which.Should().Be(entry);
    }

    [Fact]
    public async Task SetWeekAsync_WithTooFewOrTooManyEntries_Throws()
    {
        using AppDbContext db = _database.CreateContext();
        WeekPlanService service = new(db);

        Func<Task> tooFew = () => service.SetWeekAsync(Array.Empty<UpsertWeekEntryRequest>(), CancellationToken.None);
        Func<Task> tooMany = () => service.SetWeekAsync(
            Enumerable.Range(0, 29).Select(i => new UpsertWeekEntryRequest(new DateOnly(2026, 1, 1).AddDays(i), SlotKey.Breakfast, _mealId, 1m, null)).ToList(),
            CancellationToken.None);

        await tooFew.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await tooMany.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task SetWeekAsync_WithValidEntries_CreatesAndUpdatesAsAppropriate()
    {
        await SeedSlotAndMealAsync();
        DateOnly date = new(2026, 1, 5);

        using (AppDbContext db1 = _database.CreateContext())
        {
            await new WeekPlanService(db1).UpsertEntryAsync(
                new UpsertWeekEntryRequest(date, SlotKey.Breakfast, _mealId, 1m, "original"),
                CancellationToken.None);
        }

        using AppDbContext db2 = _database.CreateContext();
        WeekPlanService service = new(db2);

        var (result, count) = await service.SetWeekAsync(
            new List<UpsertWeekEntryRequest>
            {
                new(date, SlotKey.Breakfast, _mealId, 3m, "updated"),
                new(date.AddDays(1), SlotKey.Breakfast, _mealId, 1m, null)
            },
            CancellationToken.None);

        result.Should().Be(UpsertWeekEntryResult.Success);
        count.Should().Be(2);

        IReadOnlyList<WeekPlanEntryDto> all = await service.GetWeekAsync(date, date.AddDays(1), CancellationToken.None);
        all.Should().HaveCount(2);
        all.Should().Contain(e => e.Date == date && e.PortionMultiplier == 3m && e.Notes == "updated");
    }

    [Fact]
    public async Task SetWeekAsync_WithUnknownMeal_ReturnsUnknownMealWithoutPersistingValidEntries()
    {
        await SeedSlotAndMealAsync();
        using AppDbContext db = _database.CreateContext();
        WeekPlanService service = new(db);
        DateOnly date = new(2026, 1, 5);

        var (result, count) = await service.SetWeekAsync(
            new List<UpsertWeekEntryRequest>
            {
                new(date, SlotKey.Breakfast, _mealId, 1m, null),
                new(date.AddDays(1), SlotKey.Breakfast, Guid.NewGuid(), 1m, null)
            },
            CancellationToken.None);

        result.Should().Be(UpsertWeekEntryResult.UnknownMeal);
        count.Should().Be(0);
    }

    [Fact]
    public async Task DeleteEntryAsync_WithExistingEntry_RemovesIt()
    {
        await SeedSlotAndMealAsync();
        DateOnly date = new(2026, 1, 5);
        using AppDbContext db = _database.CreateContext();
        WeekPlanService service = new(db);
        await service.UpsertEntryAsync(new UpsertWeekEntryRequest(date, SlotKey.Breakfast, _mealId, 1m, null), CancellationToken.None);

        DeleteResult result = await service.DeleteEntryAsync(date, SlotKey.Breakfast, CancellationToken.None);

        result.Should().Be(DeleteResult.Success);
        (await service.GetWeekAsync(date, date, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteEntryAsync_WithUnknownEntry_ReturnsNotFound()
    {
        using AppDbContext db = _database.CreateContext();
        WeekPlanService service = new(db);

        DeleteResult result = await service.DeleteEntryAsync(new DateOnly(2026, 1, 5), SlotKey.Breakfast, CancellationToken.None);

        result.Should().Be(DeleteResult.NotFound);
    }

    public void Dispose() => _database.Dispose();
}
