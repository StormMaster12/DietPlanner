using System.Net;
using System.Text;
using System.Text.Json;
using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Slots;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DietPlanner.Tests;

[TestFixture]
public sealed class IngredientNormalizationServiceTests
{
    private TestDatabase _database = null!;

    [SetUp]
    public void SetUp() => _database = new TestDatabase();

    [TearDown]
    public void TearDown() => _database.Dispose();

    private static async Task SeedMealWithIngredientsAsync(AppDbContext db, params (string Name, decimal Quantity, string? Unit)[] ingredients)
    {
        db.Slots.Add(new Slot(SlotKey.Breakfast, "Breakfast", 0));

        Guid mealId = Guid.NewGuid();
        db.Meals.Add(new MealEntry
        {
            Id = mealId,
            Name = $"Meal {mealId}",
            SlotKey = SlotKey.Breakfast,
            Kcal = 100,
            ProteinG = 1,
            CarbsG = 1,
            FibreG = 1,
            Plants = 0,
            MfName = "Meal",
            Ingredients = ingredients.Select(i => new MealIngredient
            {
                Id = Guid.NewGuid(),
                MealId = mealId,
                Name = i.Name,
                Quantity = i.Quantity,
                Unit = i.Unit
            }).ToList()
        });
        await db.SaveChangesAsync();
    }

    private static IngredientNormalizationService CreateService(AppDbContext db, string anthropicResponseBody, string apiKey = "test-key")
    {
        StubHttpMessageHandler handler = new(anthropicResponseBody);
        HttpClient httpClient = new(handler);
        IOptions<AnthropicOptions> options = Options.Create(new AnthropicOptions { ApiKey = apiKey, Model = "claude-sonnet-4-6" });
        return new IngredientNormalizationService(db, httpClient, options, NullLogger<IngredientNormalizationService>.Instance);
    }

    private static string BuildAnthropicResponse(string normalizedJsonArray)
        => JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = normalizedJsonArray } } });

    [Test]
    public async Task NormalizeIngredientNamesAsync_WithRenamedDuplicate_RewritesMatchingRows()
    {
        using AppDbContext db = _database.CreateContext();
        await SeedMealWithIngredientsAsync(db,
            ("0% fat Greek yogurt", 1, "cup"),
            ("0% Greek yogurt", 1, "cup"));

        string normalizedJsonArray = """
            [{"original":"0% Greek yogurt","normalized":"0% Greek yogurt"},{"original":"0% fat Greek yogurt","normalized":"0% Greek yogurt"}]
            """;
        IngredientNormalizationService service = CreateService(db, BuildAnthropicResponse(normalizedJsonArray));

        IngredientNormalizationResult result = await service.NormalizeIngredientNamesAsync(CancellationToken.None);

        Assert.That(result.DistinctNamesConsidered, Is.EqualTo(2));
        Assert.That(result.Renames, Has.Count.EqualTo(1));
        Assert.That(result.Renames.Single().OriginalName, Is.EqualTo("0% fat Greek yogurt"));
        Assert.That(result.Renames.Single().NormalizedName, Is.EqualTo("0% Greek yogurt"));
        Assert.That(result.Renames.Single().AffectedCount, Is.EqualTo(1));

        List<string> namesInDb = db.MealIngredients.Select(i => i.Name).ToList();
        Assert.That(namesInDb, Has.All.EqualTo("0% Greek yogurt"));
    }

    [Test]
    public async Task NormalizeIngredientNamesAsync_WithUnchangedNames_ReportsNoRenames()
    {
        using AppDbContext db = _database.CreateContext();
        await SeedMealWithIngredientsAsync(db, ("Chicken breast", 1, null));

        string normalizedJsonArray = """[{"original":"Chicken breast","normalized":"Chicken breast"}]""";
        IngredientNormalizationService service = CreateService(db, BuildAnthropicResponse(normalizedJsonArray));

        IngredientNormalizationResult result = await service.NormalizeIngredientNamesAsync(CancellationToken.None);

        Assert.That(result.Renames, Is.Empty);
        Assert.That(db.MealIngredients.Single().Name, Is.EqualTo("Chicken breast"));
    }

    [Test]
    public async Task NormalizeIngredientNamesAsync_WithNoApiKeyConfigured_ReturnsConfigurationError()
    {
        using AppDbContext db = _database.CreateContext();
        await SeedMealWithIngredientsAsync(db, ("Chicken breast", 1, null));

        IngredientNormalizationService service = CreateService(db, BuildAnthropicResponse("[]"), apiKey: "");

        IngredientNormalizationResult result = await service.NormalizeIngredientNamesAsync(CancellationToken.None);

        Assert.That(result.Renames, Is.Empty);
        Assert.That(result.Errors.Count(e => e.Contains("not configured")), Is.EqualTo(1));
    }

    [Test]
    public async Task NormalizeIngredientNamesAsync_WithNoIngredients_ReturnsEmptyResult()
    {
        using AppDbContext db = _database.CreateContext();
        IngredientNormalizationService service = CreateService(db, BuildAnthropicResponse("[]"));

        IngredientNormalizationResult result = await service.NormalizeIngredientNamesAsync(CancellationToken.None);

        Assert.That(result.DistinctNamesConsidered, Is.EqualTo(0));
        Assert.That(result.Renames, Is.Empty);
        Assert.That(result.Errors, Is.Empty);
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
