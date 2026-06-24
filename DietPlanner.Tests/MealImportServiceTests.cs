using System.Text;
using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Slots;
using FluentAssertions;
using Xunit;

namespace DietPlanner.Tests;

public sealed class MealImportServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();

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

    [Fact]
    public async Task ImportFromCsvAsync_WithNewRows_InsertsMeals()
    {
        await SeedAllSlotsAsync();
        using AppDbContext db = _database.CreateContext();
        MealImportService service = new(db);
        string csv = "Name,SlotKey,Kcal,ProteinG,CarbsG,FibreG,Plants,MfName,ZoeNotes,Notes\n" +
                      "Oatmeal,Breakfast,300,10,40,5,2,Oatmeal MFP,Good fibre,\n" +
                      "Stew,Dinner,500,30,40,8,4,Stew MFP,,Freezes well\n";

        MealImportResult result = await service.ImportFromCsvAsync(ToStream(csv), CancellationToken.None);

        result.InsertedCount.Should().Be(2);
        result.UpdatedCount.Should().Be(0);
        result.RowErrors.Should().BeEmpty();

        MealsService mealsService = new(db);
        IReadOnlyList<MealDto> meals = await mealsService.GetMealsAsync(CancellationToken.None);
        meals.Should().HaveCount(2);
        meals.Should().Contain(m => m.Name == "Oatmeal" && m.SlotKey == SlotKey.Breakfast && m.Kcal == 300);
    }

    [Fact]
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

        result.InsertedCount.Should().Be(0);
        result.UpdatedCount.Should().Be(1);

        MealsService mealsService = new(db);
        IReadOnlyList<MealDto> meals = await mealsService.GetMealsAsync(CancellationToken.None);
        meals.Should().ContainSingle();
        meals.Single().Kcal.Should().Be(350);
    }

    [Fact]
    public async Task ImportFromCsvAsync_WithUnknownSlotKey_ReportsRowErrorAndSkipsRow()
    {
        await SeedAllSlotsAsync();
        using AppDbContext db = _database.CreateContext();
        MealImportService service = new(db);
        string csv = "Name,SlotKey,Kcal,ProteinG,CarbsG,FibreG,Plants,MfName,ZoeNotes,Notes\n" +
                      "Oatmeal,Brunch,300,10,40,5,2,Oatmeal MFP,,\n";

        MealImportResult result = await service.ImportFromCsvAsync(ToStream(csv), CancellationToken.None);

        result.InsertedCount.Should().Be(0);
        result.RowErrors.Should().ContainSingle(e => e.Contains("Row 2") && e.Contains("Brunch"));
    }

    [Fact]
    public async Task ImportFromCsvAsync_WithNonNumericKcal_ReportsRowErrorAndSkipsRow()
    {
        await SeedAllSlotsAsync();
        using AppDbContext db = _database.CreateContext();
        MealImportService service = new(db);
        string csv = "Name,SlotKey,Kcal,ProteinG,CarbsG,FibreG,Plants,MfName,ZoeNotes,Notes\n" +
                      "Oatmeal,Breakfast,abc,10,40,5,2,Oatmeal MFP,,\n";

        MealImportResult result = await service.ImportFromCsvAsync(ToStream(csv), CancellationToken.None);

        result.InsertedCount.Should().Be(0);
        result.RowErrors.Should().ContainSingle(e => e.Contains("Row 2") && e.Contains("Kcal"));
    }

    [Fact]
    public async Task ImportFromCsvAsync_WithOneBadRowAndOneGoodRow_ImportsGoodRowAndReportsBadOne()
    {
        await SeedAllSlotsAsync();
        using AppDbContext db = _database.CreateContext();
        MealImportService service = new(db);
        string csv = "Name,SlotKey,Kcal,ProteinG,CarbsG,FibreG,Plants,MfName,ZoeNotes,Notes\n" +
                      "Oatmeal,Breakfast,300,10,40,5,2,Oatmeal MFP,,\n" +
                      "Stew,Dinner,300,abc,40,5,2,Stew MFP,,\n";

        MealImportResult result = await service.ImportFromCsvAsync(ToStream(csv), CancellationToken.None);

        result.InsertedCount.Should().Be(1);
        result.RowErrors.Should().ContainSingle(e => e.Contains("Row 3"));
    }

    public void Dispose() => _database.Dispose();
}
