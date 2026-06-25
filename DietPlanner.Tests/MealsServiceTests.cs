using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Slots;

namespace DietPlanner.Tests;

[TestFixture]
public sealed class MealsServiceTests
{
    private TestDatabase _database = null!;

    [SetUp]
    public void SetUp() => _database = new TestDatabase();

    [TearDown]
    public void TearDown() => _database.Dispose();

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

    [Test]
    public async Task UpsertMealAsync_WithUnknownSlot_ReturnsInvalidSlot()
    {
        using AppDbContext db = _database.CreateContext();
        MealsService service = new(db);

        var (result, meal) = await service.UpsertMealAsync(NewMealRequest(), CancellationToken.None);

        Assert.That(result, Is.EqualTo(UpsertMealResult.InvalidSlot));
        Assert.That(meal, Is.Null);
    }

    [Test]
    public async Task UpsertMealAsync_WithNewMealId_CreatesMealWithIngredients()
    {
        await SeedBreakfastSlotAsync();
        using AppDbContext db = _database.CreateContext();
        MealsService service = new(db);

        var (result, meal) = await service.UpsertMealAsync(NewMealRequest(), CancellationToken.None);

        Assert.That(result, Is.EqualTo(UpsertMealResult.Success));
        Assert.That(meal!.Name, Is.EqualTo("Oatmeal"));
        Assert.That(meal.Ingredients.Count(i => i.Name == "Oats" && i.Quantity == 80), Is.EqualTo(1));
    }

    [Test]
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

        Assert.That(meal!.Name, Is.EqualTo("Oatmeal"));
        Assert.That(meal.Ingredients.Single().Name, Is.EqualTo("Oats"));
        Assert.That(meal.Ingredients.Single().Unit, Is.EqualTo("g"));
    }

    [Test]
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

        Assert.That(result, Is.EqualTo(UpsertMealResult.Success));
        Assert.That(meal!.Name, Is.EqualTo("Porridge"));
        Assert.That(meal.Kcal, Is.EqualTo(350));
        Assert.That(meal.Ingredients, Has.Count.EqualTo(2));
        Assert.That(meal.Ingredients.Any(i => i.Name == "Porridge oats"), Is.True);
        Assert.That(meal.Ingredients.Any(i => i.Name == "Milk"), Is.True);
    }

    [Test]
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

        Assert.DoesNotThrowAsync(async () => await new MealsService(db2).UpsertMealAsync(
            NewMealRequest(mealId) with { Ingredients = new List<UpsertMealIngredientRequest> { new("New ingredient", 1, null) } },
            CancellationToken.None));
    }

    [Test]
    public async Task GetMealsAsync_ReturnsAllMealsWithIngredients()
    {
        await SeedBreakfastSlotAsync();
        using AppDbContext db = _database.CreateContext();
        MealsService service = new(db);
        await service.UpsertMealAsync(NewMealRequest(name: "Oatmeal"), CancellationToken.None);
        await service.UpsertMealAsync(NewMealRequest(name: "Yoghurt"), CancellationToken.None);

        IReadOnlyList<MealDto> meals = await service.GetMealsAsync(CancellationToken.None);

        Assert.That(meals, Has.Count.EqualTo(2));
        Assert.That(meals.Any(m => m.Name == "Oatmeal" && m.Ingredients.Count == 1), Is.True);
    }

    [Test]
    public async Task GetMealAsync_WithUnknownId_ReturnsNull()
    {
        using AppDbContext db = _database.CreateContext();
        MealsService service = new(db);

        MealDto? result = await service.GetMealAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task DeleteMealAsync_WithExistingMeal_RemovesMeal()
    {
        await SeedBreakfastSlotAsync();
        using AppDbContext db = _database.CreateContext();
        MealsService service = new(db);
        var (_, meal) = await service.UpsertMealAsync(NewMealRequest(), CancellationToken.None);

        DeleteResult result = await service.DeleteMealAsync(meal!.MealId, CancellationToken.None);

        Assert.That(result, Is.EqualTo(DeleteResult.Success));
        Assert.That(await service.GetMealsAsync(CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task DeleteMealAsync_WithUnknownId_ReturnsNotFound()
    {
        using AppDbContext db = _database.CreateContext();
        MealsService service = new(db);

        DeleteResult result = await service.DeleteMealAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.That(result, Is.EqualTo(DeleteResult.NotFound));
    }

}
