using DietPlanner.Endpoints.DayPlan;
using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Settings;
using DietPlanner.Endpoints.Slots;
using DietPlanner.Endpoints.WeekPlan;
using Microsoft.EntityFrameworkCore;

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


    private async Task<Guid> SeedPlannedMealAsync(params (DateOnly Date, decimal PortionMultiplier)[] plannedOn)
    {
        Guid mealId = Guid.NewGuid();
        using AppDbContext db = _database.CreateContext();
        db.Slots.Add(new Slot(SlotKey.Breakfast, "Breakfast", 1));
        db.Meals.Add(new MealEntry
        {
            Id = mealId,
            Name = "Porridge",
            SlotKey = SlotKey.Breakfast,
            Kcal = 300,
            MfName = "Porridge MFP",
            Ingredients = new List<MealIngredient>
            {
                new() { Id = Guid.NewGuid(), MealId = mealId, Name = "Oats", Quantity = 50, Unit = "g" }
            }
        });
        foreach ((DateOnly date, decimal portionMultiplier) in plannedOn)
        {
            db.WeekPlanEntries.Add(new WeekPlanEntry(Guid.NewGuid(), date, SlotKey.Breakfast, mealId, portionMultiplier, null));
        }

        await db.SaveChangesAsync();
        return mealId;
    }

    /// <summary>Saves suggestions for the meal as it currently stands, as the suggestion service would after an AI call.</summary>
    private async Task SaveSuggestionsAsync(Guid mealId, params MealSuggestedAddition[] additions)
    {
        using AppDbContext db = _database.CreateContext();
        MealEntry meal = db.Meals.Include(m => m.Ingredients).Single(m => m.Id == mealId);
        db.MealAdditionSuggestionSets.Add(new MealAdditionSuggestionSet
        {
            MealId = mealId,
            MealFingerprint = MealAdditionFingerprint.Compute(meal),
            CreatedAtUtc = DateTime.UtcNow,
            Additions = additions.ToList(),
        });
        await db.SaveChangesAsync();
    }

    private static MealSuggestedAddition Addition(Guid mealId, int sortOrder, string name, decimal quantity, string? unit)
        => new() { Id = Guid.NewGuid(), MealId = mealId, SortOrder = sortOrder, Name = name, Quantity = quantity, Unit = unit, Reason = $"{name} adds fibre." };

    [Test]
    public async Task GetDayPlanAsync_WithNoSavedSuggestions_ReturnsNullSuggestedAdditions()
    {
        DateOnly date = new(2026, 1, 5);
        await SeedPlannedMealAsync((date, 1m));

        using AppDbContext db = _database.CreateContext();
        DayPlanResponseDto result = await new DayPlanService(db, new SettingsService(db)).GetDayPlanAsync(date, CancellationToken.None);

        Assert.That(result.Meals.Single().SuggestedAdditions, Is.Null);
    }

    [Test]
    public async Task GetDayPlanAsync_WithSavedSuggestions_ReturnsThemInSuggestedOrder()
    {
        DateOnly date = new(2026, 1, 5);
        Guid mealId = await SeedPlannedMealAsync((date, 1m));
        await SaveSuggestionsAsync(mealId,
            Addition(mealId, 1, "Apple", 1m, null),
            Addition(mealId, 0, "Chia seeds", 1m, "tbsp"));

        using AppDbContext db = _database.CreateContext();
        DayPlanResponseDto result = await new DayPlanService(db, new SettingsService(db)).GetDayPlanAsync(date, CancellationToken.None);

        Assert.That(result.Meals.Single().SuggestedAdditions, Is.EqualTo(new[]
        {
            new SuggestedMealAddition("Chia seeds", 1m, "tbsp", "Chia seeds adds fibre."),
            new SuggestedMealAddition("Apple", 1m, null, "Apple adds fibre."),
        }));
    }

    [Test]
    public async Task GetDayPlanAsync_WithSavedEmptySuggestions_ReturnsEmptyListRatherThanNull()
    {
        DateOnly date = new(2026, 1, 5);
        Guid mealId = await SeedPlannedMealAsync((date, 1m));
        await SaveSuggestionsAsync(mealId);

        using AppDbContext db = _database.CreateContext();
        DayPlanResponseDto result = await new DayPlanService(db, new SettingsService(db)).GetDayPlanAsync(date, CancellationToken.None);

        Assert.That(result.Meals.Single().SuggestedAdditions, Is.Not.Null.And.Empty);
    }

    [Test]
    public async Task GetDayPlanAsync_SameMealOnDifferentDays_ShowsSameSuggestionsScaledToEachPortion()
    {
        DateOnly monday = new(2026, 1, 5);
        DateOnly tuesday = monday.AddDays(1);
        Guid mealId = await SeedPlannedMealAsync((monday, 1m), (tuesday, 2m));
        await SaveSuggestionsAsync(mealId, Addition(mealId, 0, "Chia seeds", 1.5m, "tbsp"));

        using AppDbContext db = _database.CreateContext();
        DayPlanService service = new(db, new SettingsService(db));
        DayPlanResponseDto mondayPlan = await service.GetDayPlanAsync(monday, CancellationToken.None);
        DayPlanResponseDto tuesdayPlan = await service.GetDayPlanAsync(tuesday, CancellationToken.None);

        Assert.That(mondayPlan.Meals.Single().SuggestedAdditions!.Single().Quantity, Is.EqualTo(1.5m));
        Assert.That(tuesdayPlan.Meals.Single().SuggestedAdditions!.Single().Quantity, Is.EqualTo(3m));
    }

    [Test]
    public async Task GetDayPlanAsync_WhenMealEditedSinceSuggestionsWereSaved_HidesThem()
    {
        DateOnly date = new(2026, 1, 5);
        Guid mealId = await SeedPlannedMealAsync((date, 1m));
        await SaveSuggestionsAsync(mealId, Addition(mealId, 0, "Chia seeds", 1m, "tbsp"));

        using (AppDbContext editDb = _database.CreateContext())
        {
            editDb.MealIngredients.Add(new MealIngredient { Id = Guid.NewGuid(), MealId = mealId, Name = "Banana", Quantity = 1m });
            await editDb.SaveChangesAsync();
        }

        using AppDbContext db = _database.CreateContext();
        DayPlanResponseDto result = await new DayPlanService(db, new SettingsService(db)).GetDayPlanAsync(date, CancellationToken.None);

        Assert.That(result.Meals.Single().SuggestedAdditions, Is.Null);
    }

    [Test]
    public async Task GetDayPlanAsync_SuggestedAdditions_DoNotChangeMealIngredientsOrTotals()
    {
        DateOnly date = new(2026, 1, 5);
        Guid mealId = await SeedPlannedMealAsync((date, 1m));
        await SaveSuggestionsAsync(mealId, Addition(mealId, 0, "Chia seeds", 1m, "tbsp"));

        using AppDbContext db = _database.CreateContext();
        DayPlanResponseDto result = await new DayPlanService(db, new SettingsService(db)).GetDayPlanAsync(date, CancellationToken.None);

        Assert.That(result.Meals.Single().Ingredients.Select(i => i.Name), Is.EqualTo(new[] { "Oats" }));
        Assert.That(result.Totals.Kcal, Is.EqualTo(300));
    }
}
