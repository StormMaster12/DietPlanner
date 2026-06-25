using System.Text;
using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Slots;

namespace DietPlanner.Tests;

[TestFixture]
public sealed class MealImportServiceTests
{
    private TestDatabase _database = null!;

    [SetUp]
    public void SetUp() => _database = new TestDatabase();

    [TearDown]
    public void TearDown() => _database.Dispose();

    private static Stream ToStream(string csv) => new MemoryStream(Encoding.UTF8.GetBytes(csv));

    private async Task SeedAllSlotsAsync()
    {
        using AppDbContext db = _database.CreateContext();
        foreach (SlotKey key in Enum.GetValues<SlotKey>())
        {
            db.Slots.Add(new Slot(key, key.ToString(), (int)key));
        }
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task ImportFromCsvAsync_WithNewRows_InsertsMeals()
    {
        await SeedAllSlotsAsync();
        using AppDbContext db = _database.CreateContext();
        MealImportService service = new(db);
        string csv = "Name,SlotKey,Kcal,ProteinG,CarbsG,FibreG,Plants,MfName,ZoeNotes,Notes\n" +
                      "Oatmeal,Breakfast,300,10,40,5,2,Oatmeal MFP,Good fibre,\n" +
                      "Stew,Dinner,500,30,40,8,4,Stew MFP,,Freezes well\n";

        MealImportResult result = await service.ImportFromCsvAsync(ToStream(csv), CancellationToken.None);

        Assert.That(result.InsertedCount, Is.EqualTo(2));
        Assert.That(result.UpdatedCount, Is.EqualTo(0));
        Assert.That(result.RowErrors, Is.Empty);

        MealsService mealsService = new(db);
        IReadOnlyList<MealDto> meals = await mealsService.GetMealsAsync(CancellationToken.None);
        Assert.That(meals, Has.Count.EqualTo(2));
        Assert.That(meals.Any(m => m.Name == "Oatmeal" && m.SlotKey == SlotKey.Breakfast && m.Kcal == 300), Is.True);
    }

    [Test]
    public async Task ImportFromCsvAsync_WithExistingMatchingNameAndSlot_UpdatesInsteadOfDuplicating()
    {
        await SeedAllSlotsAsync();
        using AppDbContext db = _database.CreateContext();
        MealImportService service = new(db);
        string firstCsv = "Name,SlotKey,Kcal,ProteinG,CarbsG,FibreG,Plants,MfName,ZoeNotes,Notes\n" +
                           "Oatmeal,Breakfast,300,10,40,5,2,Oatmeal MFP,,\n";
        await service.ImportFromCsvAsync(ToStream(firstCsv), CancellationToken.None);

        string secondCsv = "Name,SlotKey,Kcal,ProteinG,CarbsG,FibreG,Plants,MfName,ZoeNotes,Notes\n" +
                            "oatmeal,breakfast,350,12,45,6,2,Oatmeal MFP v2,,\n";
        MealImportResult result = await service.ImportFromCsvAsync(ToStream(secondCsv), CancellationToken.None);

        Assert.That(result.InsertedCount, Is.EqualTo(0));
        Assert.That(result.UpdatedCount, Is.EqualTo(1));

        MealsService mealsService = new(db);
        IReadOnlyList<MealDto> meals = await mealsService.GetMealsAsync(CancellationToken.None);
        Assert.That(meals, Has.Count.EqualTo(1));
        Assert.That(meals.Single().Kcal, Is.EqualTo(350));
    }

    [Test]
    public async Task ImportFromCsvAsync_WithUnknownSlotKey_ReportsRowErrorAndSkipsRow()
    {
        await SeedAllSlotsAsync();
        using AppDbContext db = _database.CreateContext();
        MealImportService service = new(db);
        string csv = "Name,SlotKey,Kcal,ProteinG,CarbsG,FibreG,Plants,MfName,ZoeNotes,Notes\n" +
                      "Oatmeal,Brunch,300,10,40,5,2,Oatmeal MFP,,\n";

        MealImportResult result = await service.ImportFromCsvAsync(ToStream(csv), CancellationToken.None);

        Assert.That(result.InsertedCount, Is.EqualTo(0));
        Assert.That(result.RowErrors.Count(e => e.Contains("Row 2") && e.Contains("Brunch")), Is.EqualTo(1));
    }

    [Test]
    public async Task ImportFromCsvAsync_WithNonNumericKcal_ReportsRowErrorAndSkipsRow()
    {
        await SeedAllSlotsAsync();
        using AppDbContext db = _database.CreateContext();
        MealImportService service = new(db);
        string csv = "Name,SlotKey,Kcal,ProteinG,CarbsG,FibreG,Plants,MfName,ZoeNotes,Notes\n" +
                      "Oatmeal,Breakfast,abc,10,40,5,2,Oatmeal MFP,,\n";

        MealImportResult result = await service.ImportFromCsvAsync(ToStream(csv), CancellationToken.None);

        Assert.That(result.InsertedCount, Is.EqualTo(0));
        Assert.That(result.RowErrors.Count(e => e.Contains("Row 2") && e.Contains("Kcal")), Is.EqualTo(1));
    }

    [Test]
    public async Task ImportFromCsvAsync_WithOneBadRowAndOneGoodRow_ImportsGoodRowAndReportsBadOne()
    {
        await SeedAllSlotsAsync();
        using AppDbContext db = _database.CreateContext();
        MealImportService service = new(db);
        string csv = "Name,SlotKey,Kcal,ProteinG,CarbsG,FibreG,Plants,MfName,ZoeNotes,Notes\n" +
                      "Oatmeal,Breakfast,300,10,40,5,2,Oatmeal MFP,,\n" +
                      "Stew,Dinner,300,abc,40,5,2,Stew MFP,,\n";

        MealImportResult result = await service.ImportFromCsvAsync(ToStream(csv), CancellationToken.None);

        Assert.That(result.InsertedCount, Is.EqualTo(1));
        Assert.That(result.RowErrors.Count(e => e.Contains("Row 3")), Is.EqualTo(1));
    }

}
