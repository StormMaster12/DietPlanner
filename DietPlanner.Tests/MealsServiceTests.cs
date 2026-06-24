using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Slots;
using FluentAssertions;
using Xunit;

namespace DietPlanner.Tests;

public sealed class MealsServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();

    private async Task SeedBreakfastSlotAsync()
    {
        using AppDbContext db = _database.CreateContext();
        db.Slots.Add(new Slot(SlotKey.Breakfast, "Breakfast", 1));
        await db.SaveChangesAsync();
    }

    private static UpsertMealRequest NewMealRequest(Guid? mealId = null, string name = "Oatmeal") => new(
        mealId ?? Guid.NewGuid(),
        name,
        SlotKey.Breakfast,
        Kcal: 300,
        ProteinG: 10,
        CarbsG: 40,
        FibreG: 5,
        Plants: 2,
        ZoeNotes: "Good for gut health",
        MfName: "Oatmeal MFP",
        Notes: "Add cinnamon",
        Ingredients: new List<UpsertMealIngredientRequest> { new("Oats", 80, "g") });

    [Fact]
    public async Task UpsertMealAsync_WithUnknownSlot_ReturnsInvalidSlot()
    {
        using AppDbContext db = _database.CreateContext();
        MealsService service = new(db);

        var (result, meal) = await service.UpsertMealAsync(NewMealRequest(), CancellationToken.None);

        result.Should().Be(UpsertMealResult.InvalidSlot);
        meal.Should().BeNull();
    }

    [Fact]
    public async Task UpsertMealAsync_WithNewMealId_CreatesMealWithIngredients()
    {
        await SeedBreakfastSlotAsync();
        using AppDbContext db = _database.CreateContext();
        MealsService service = new(db);

        var (result, meal) = await service.UpsertMealAsync(NewMealRequest(), CancellationToken.None);

        result.Should().Be(UpsertMealResult.Success);
        meal!.Name.Should().Be("Oatmeal");
        meal.Ingredients.Should().ContainSingle(i => i.Name == "Oats" && i.Quantity == 80);
    }

    [Fact]
    public async Task UpsertMealAsync_TrimsNameAndIngredientFields()
    {
        await SeedBreakfastSlotAsync();
        using AppDbContext db = _database.CreateContext();
        MealsService service = new(db);

        UpsertMealRequest request = NewMealRequest(name: "  Oatmeal  ") with
        {
            Ingredients = new List<UpsertMealIngredientRequest> { new("  Oats  ", 80, "  g  ") }
        };

        var (_, meal) = await service.UpsertMealAsync(request, CancellationToken.None);

        meal!.Name.Should().Be("Oatmeal");
        meal.Ingredients.Single().Name.Should().Be("Oats");
        meal.Ingredients.Single().Unit.Should().Be("g");
    }

    [Fact]
    public async Task UpsertMealAsync_WithExistingMealId_UpdatesFieldsAndReplacesIngredients()
    {
        await SeedBreakfastSlotAsync();
        Guid mealId = Guid.NewGuid();

        using (AppDbContext db1 = _database.CreateContext())
        {
            await new MealsService(db1).UpsertMealAsync(NewMealRequest(mealId), CancellationToken.None);
        }

        using AppDbContext db2 = _database.CreateContext();
        MealsService service = new(db2);

        UpsertMealRequest updateRequest = NewMealRequest(mealId, "Porridge") with
        {
            Kcal = 350,
            Ingredients = new List<UpsertMealIngredientRequest> { new("Porridge oats", 90, "g"), new("Milk", 200, "ml") }
        };

        var (result, meal) = await service.UpsertMealAsync(updateRequest, CancellationToken.None);

        result.Should().Be(UpsertMealResult.Success);
        meal!.Name.Should().Be("Porridge");
        meal.Kcal.Should().Be(350);
        meal.Ingredients.Should().HaveCount(2);
        meal.Ingredients.Should().Contain(i => i.Name == "Porridge oats");
        meal.Ingredients.Should().Contain(i => i.Name == "Milk");
    }

    [Fact]
    public async Task UpsertMealAsync_UpdateDoesNotThrowConcurrencyException_AcrossFreshContexts()
    {
        // Regression coverage for the ingredient-replacement approach: re-adding new ingredient
        // rows via the DbSet (not just the navigation collection) avoids EF mistaking client-set
        // guid keys for existing rows and throwing DbUpdateConcurrencyException on save.
        await SeedBreakfastSlotAsync();
        Guid mealId = Guid.NewGuid();

        using (AppDbContext db1 = _database.CreateContext())
        {
            await new MealsService(db1).UpsertMealAsync(NewMealRequest(mealId), CancellationToken.None);
        }

        using AppDbContext db2 = _database.CreateContext();
        Func<Task> act = async () => await new MealsService(db2).UpsertMealAsync(
            NewMealRequest(mealId) with { Ingredients = new List<UpsertMealIngredientRequest> { new("New ingredient", 1, null) } },
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetMealsAsync_ReturnsAllMealsWithIngredients()
    {
        await SeedBreakfastSlotAsync();
        using AppDbContext db = _database.CreateContext();
        MealsService service = new(db);
        await service.UpsertMealAsync(NewMealRequest(name: "Oatmeal"), CancellationToken.None);
        await service.UpsertMealAsync(NewMealRequest(name: "Yoghurt"), CancellationToken.None);

        IReadOnlyList<MealDto> meals = await service.GetMealsAsync(CancellationToken.None);

        meals.Should().HaveCount(2);
        meals.Should().Contain(m => m.Name == "Oatmeal" && m.Ingredients.Count == 1);
    }

    [Fact]
    public async Task GetMealAsync_WithUnknownId_ReturnsNull()
    {
        using AppDbContext db = _database.CreateContext();
        MealsService service = new(db);

        MealDto? result = await service.GetMealAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteMealAsync_WithExistingMeal_RemovesMeal()
    {
        await SeedBreakfastSlotAsync();
        using AppDbContext db = _database.CreateContext();
        MealsService service = new(db);
        var (_, meal) = await service.UpsertMealAsync(NewMealRequest(), CancellationToken.None);

        DeleteResult result = await service.DeleteMealAsync(meal!.MealId, CancellationToken.None);

        result.Should().Be(DeleteResult.Success);
        (await service.GetMealsAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteMealAsync_WithUnknownId_ReturnsNotFound()
    {
        using AppDbContext db = _database.CreateContext();
        MealsService service = new(db);

        DeleteResult result = await service.DeleteMealAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().Be(DeleteResult.NotFound);
    }

    public void Dispose() => _database.Dispose();
}
