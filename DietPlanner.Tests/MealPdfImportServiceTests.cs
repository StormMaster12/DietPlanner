using System.Net;
using System.Text;
using System.Text.Json;
using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Slots;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace DietPlanner.Tests;

[TestFixture]
public sealed class MealPdfImportServiceTests
{
    private TestDatabase _database = null!;

    [SetUp]
    public void SetUp() => _database = new TestDatabase();

    [TearDown]
    public void TearDown() => _database.Dispose();

    private async Task SeedAllSlotsAsync()
    {
        using AppDbContext db = _database.CreateContext();
        foreach (SlotKey key in Enum.GetValues<SlotKey>())
        {
            db.Slots.Add(new Slot(key, key.ToString(), (int)key));
        }
        await db.SaveChangesAsync();
    }

    private static Stream BuildPdf(string text)
    {
        PdfDocumentBuilder builder = new();
        PdfPageBuilder page = builder.AddPage(PageSize.A4);
        PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText(text, 12, new PdfPoint(25, 700), font);
        return new MemoryStream(builder.Build());
    }

    private static MealPdfImportService CreateService(AppDbContext db, string anthropicResponseBody, string apiKey = "test-key")
    {
        StubHttpMessageHandler handler = new(anthropicResponseBody);
        HttpClient httpClient = new(handler);
        IOptions<AnthropicOptions> options = Options.Create(new AnthropicOptions { ApiKey = apiKey, Model = "claude-sonnet-4-6" });
        return new MealPdfImportService(db, httpClient, options, NullLogger<MealPdfImportService>.Instance);
    }

    private static string BuildAnthropicResponse(string extractedJsonArray)
        => JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = extractedJsonArray } } });

    [Test]
    public async Task ImportFromPdfAsync_WithValidLlmResponse_InsertsMeals()
    {
        await SeedAllSlotsAsync();
        using AppDbContext db = _database.CreateContext();

        string extractedJsonArray = """
            [{"name":"Oatmeal","slotKey":"Breakfast","kcal":300,"proteinG":10,"carbsG":40,"fibreG":5,"plants":2,"mfName":"Oatmeal MFP","zoeNotes":"Good fibre","notes":null}]
            """;
        MealPdfImportService service = CreateService(db, BuildAnthropicResponse(extractedJsonArray));

        MealImportResult result = await service.ImportFromPdfAsync(BuildPdf("Breakfast: oatmeal with berries."), CancellationToken.None);

        Assert.That(result.InsertedCount, Is.EqualTo(1));
        Assert.That(result.UpdatedCount, Is.EqualTo(0));
        Assert.That(result.RowErrors, Is.Empty);

        MealsService mealsService = new(db);
        IReadOnlyList<MealDto> meals = await mealsService.GetMealsAsync(CancellationToken.None);
        Assert.That(meals.Any(m => m.Name == "Oatmeal" && m.SlotKey == SlotKey.Breakfast && m.Kcal == 300), Is.True);
    }

    [Test]
    public async Task ImportFromPdfAsync_WithExistingMatchingNameAndSlot_UpdatesInsteadOfDuplicating()
    {
        await SeedAllSlotsAsync();
        using AppDbContext db = _database.CreateContext();

        string firstExtractedJson = """
            [{"name":"Oatmeal","slotKey":"Breakfast","kcal":300,"proteinG":10,"carbsG":40,"fibreG":5,"plants":2,"mfName":"Oatmeal MFP","zoeNotes":null,"notes":null}]
            """;
        await CreateService(db, BuildAnthropicResponse(firstExtractedJson))
            .ImportFromPdfAsync(BuildPdf("Breakfast: oatmeal."), CancellationToken.None);

        string secondExtractedJson = """
            [{"name":"oatmeal","slotKey":"breakfast","kcal":350,"proteinG":12,"carbsG":45,"fibreG":6,"plants":2,"mfName":"Oatmeal MFP v2","zoeNotes":null,"notes":null}]
            """;
        MealImportResult result = await CreateService(db, BuildAnthropicResponse(secondExtractedJson))
            .ImportFromPdfAsync(BuildPdf("Breakfast: oatmeal, bigger portion."), CancellationToken.None);

        Assert.That(result.InsertedCount, Is.EqualTo(0));
        Assert.That(result.UpdatedCount, Is.EqualTo(1));

        MealsService mealsService = new(db);
        IReadOnlyList<MealDto> meals = await mealsService.GetMealsAsync(CancellationToken.None);
        Assert.That(meals, Has.Count.EqualTo(1));
        Assert.That(meals.Single().Kcal, Is.EqualTo(350));
    }

    [Test]
    public async Task ImportFromPdfAsync_WithUnknownSlotKeyFromLlm_ReportsItemErrorAndSkipsRow()
    {
        await SeedAllSlotsAsync();
        using AppDbContext db = _database.CreateContext();

        string extractedJsonArray = """
            [{"name":"Oatmeal","slotKey":"Brunch","kcal":300,"proteinG":10,"carbsG":40,"fibreG":5,"plants":2,"mfName":"Oatmeal MFP","zoeNotes":null,"notes":null}]
            """;
        MealPdfImportService service = CreateService(db, BuildAnthropicResponse(extractedJsonArray));

        MealImportResult result = await service.ImportFromPdfAsync(BuildPdf("Oatmeal for brunch."), CancellationToken.None);

        Assert.That(result.InsertedCount, Is.EqualTo(0));
        Assert.That(result.RowErrors.Count(e => e.Contains("Item 1") && e.Contains("Brunch")), Is.EqualTo(1));
    }

    [Test]
    public async Task ImportFromPdfAsync_WithNoApiKeyConfigured_ReturnsConfigurationError()
    {
        await SeedAllSlotsAsync();
        using AppDbContext db = _database.CreateContext();

        MealPdfImportService service = CreateService(db, BuildAnthropicResponse("[]"), apiKey: "");

        MealImportResult result = await service.ImportFromPdfAsync(BuildPdf("Some meal text."), CancellationToken.None);

        Assert.That(result.InsertedCount, Is.EqualTo(0));
        Assert.That(result.RowErrors.Count(e => e.Contains("not configured")), Is.EqualTo(1));
    }

    [Test]
    public async Task ImportFromPdfAsync_WithPdfContainingNoText_ReturnsNoExtractableTextError()
    {
        await SeedAllSlotsAsync();
        using AppDbContext db = _database.CreateContext();

        PdfDocumentBuilder builder = new();
        builder.AddPage(PageSize.A4);
        using MemoryStream blankPdf = new(builder.Build());

        MealPdfImportService service = CreateService(db, BuildAnthropicResponse("[]"));

        MealImportResult result = await service.ImportFromPdfAsync(blankPdf, CancellationToken.None);

        Assert.That(result.InsertedCount, Is.EqualTo(0));
        Assert.That(result.RowErrors.Count(e => e.Contains("no extractable text")), Is.EqualTo(1));
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public StubHttpMessageHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
