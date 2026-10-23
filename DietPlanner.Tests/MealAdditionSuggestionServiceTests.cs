using System.Net;
using System.Text;
using System.Text.Json;
using DietPlanner.Endpoints.DayPlan;
using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Slots;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DietPlanner.Tests;

[TestFixture]
public sealed class MealAdditionSuggestionServiceTests
{
    private TestDatabase _testDatabase = null!;

    [SetUp]
    public void SetUp()
    {
        _testDatabase = new TestDatabase();
    }

    [TearDown]
    public void TearDown()
    {
        _testDatabase.Dispose();
    }

    private MealAdditionSuggestionService CreateService(HttpMessageHandler handler, string apiKey = "test-key")
    {
        HttpClient httpClient = new(handler);
        IOptions<AnthropicOptions> options = Options.Create(new AnthropicOptions { ApiKey = apiKey, Model = "claude-sonnet-4-6" });
        AnthropicApiService anthropicApi = new(httpClient, options);
        return new MealAdditionSuggestionService(anthropicApi, _testDatabase.CreateContext(), NullLogger<MealAdditionSuggestionService>.Instance);
    }

    private static string BuildAnthropicResponse(string suggestionsJsonArray)
        => JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = suggestionsJsonArray } } });

    private const string ChiaSeedsSuggestion = """
        [{"ingredient":"Chia seeds","quantity":1,"unit":"tbsp","reason":"Adds fibre without changing the flavour much."}]
        """;

    private async Task<Guid> SeedMealAsync(string name = "Porridge", string? notes = "Make it creamy")
    {
        Guid mealId = Guid.NewGuid();
        using AppDbContext db = _testDatabase.CreateContext();
        if (!db.Slots.Any())
        {
            db.Slots.Add(new Slot(SlotKey.Breakfast, "Breakfast", 1));
        }

        db.Meals.Add(new MealEntry
        {
            Id = mealId,
            Name = name,
            SlotKey = SlotKey.Breakfast,
            Kcal = 300,
            MfName = name,
            ZoeNotes = "Good for gut health",
            Notes = notes,
            Ingredients = [new MealIngredient { Id = Guid.NewGuid(), MealId = mealId, Name = "Oats", Quantity = 50m, Unit = "g" }],
        });
        await db.SaveChangesAsync();
        return mealId;
    }

    private static MealAdditionSuggestionRequest BuildRequest(Guid mealId, int fibreShortfallG = 8, int plantsShortfall = 2)
        => new(mealId, fibreShortfallG, plantsShortfall);

    private List<MealSuggestedAddition> GetSavedAdditions(Guid mealId)
    {
        using AppDbContext db = _testDatabase.CreateContext();
        return db.MealSuggestedAdditions.Where(a => a.MealId == mealId).OrderBy(a => a.SortOrder).ToList();
    }

    [Test]
    public async Task SuggestAdditionsAsync_WithSuggestions_ReturnsThemAsStructuredIngredients()
    {
        Guid mealId = await SeedMealAsync();
        MealAdditionSuggestionService service = CreateService(new StubHttpMessageHandler(BuildAnthropicResponse(ChiaSeedsSuggestion)));

        MealAdditionSuggestionResult result = await service.SuggestAdditionsAsync(BuildRequest(mealId), CancellationToken.None);

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Suggestions, Is.EqualTo(new[]
        {
            new SuggestedMealAddition("Chia seeds", 1m, "tbsp", "Adds fibre without changing the flavour much."),
        }));
    }

    [Test]
    public async Task SuggestAdditionsAsync_WithSuggestions_SavesThemAgainstTheMeal()
    {
        Guid mealId = await SeedMealAsync();
        string suggestions = """
            [
              {"ingredient":"Chia seeds","quantity":1,"unit":"tbsp","reason":"Adds fibre."},
              {"ingredient":"Apple","quantity":1,"unit":null,"reason":"Another plant."}
            ]
            """;
        MealAdditionSuggestionService service = CreateService(new StubHttpMessageHandler(BuildAnthropicResponse(suggestions)));

        await service.SuggestAdditionsAsync(BuildRequest(mealId), CancellationToken.None);

        List<MealSuggestedAddition> saved = GetSavedAdditions(mealId);
        Assert.That(saved.Select(a => (a.Name, a.Quantity, a.Unit, a.Reason)), Is.EqualTo(new[]
        {
            ("Chia seeds", 1m, (string?)"tbsp", "Adds fibre."),
            ("Apple", 1m, (string?)null, "Another plant."),
        }));
    }

    [Test]
    public async Task SuggestAdditionsAsync_SendsTheMealsSinglePortionContentsAndTheDaysShortfall()
    {
        Guid mealId = await SeedMealAsync();
        StubHttpMessageHandler handler = new(BuildAnthropicResponse("[]"));

        await CreateService(handler).SuggestAdditionsAsync(BuildRequest(mealId, fibreShortfallG: 11, plantsShortfall: 3), CancellationToken.None);

        using JsonDocument requestBody = JsonDocument.Parse(handler.LastRequestBody!);
        string userContent = requestBody.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
        using JsonDocument payload = JsonDocument.Parse(userContent);
        Assert.That(payload.RootElement.GetProperty("mealName").GetString(), Is.EqualTo("Porridge"));
        Assert.That(payload.RootElement.GetProperty("notes").GetString(), Is.EqualTo("Make it creamy"));
        Assert.That(payload.RootElement.GetProperty("fibreShortfallG").GetInt32(), Is.EqualTo(11));
        Assert.That(payload.RootElement.GetProperty("plantsShortfall").GetInt32(), Is.EqualTo(3));
        JsonElement ingredient = payload.RootElement.GetProperty("ingredients")[0];
        Assert.That(ingredient.GetProperty("name").GetString(), Is.EqualTo("Oats"));
        Assert.That(ingredient.GetProperty("quantity").GetDecimal(), Is.EqualTo(50m));
    }

    [Test]
    public async Task SuggestAdditionsAsync_WithNoApiKeyConfigured_ReturnsConfigurationErrorAndSavesNothing()
    {
        Guid mealId = await SeedMealAsync();
        MealAdditionSuggestionService service = CreateService(new StubHttpMessageHandler(BuildAnthropicResponse("[]")), apiKey: "");

        MealAdditionSuggestionResult result = await service.SuggestAdditionsAsync(BuildRequest(mealId), CancellationToken.None);

        Assert.That(result.Suggestions, Is.Empty);
        Assert.That(result.Errors.Count(e => e.Contains("not configured")), Is.EqualTo(1));
        using AppDbContext db = _testDatabase.CreateContext();
        Assert.That(db.MealAdditionSuggestionSets.Count(), Is.EqualTo(0));
    }

    [Test]
    public async Task SuggestAdditionsAsync_ForUnknownMeal_ReturnsErrorWithoutCallingAnthropic()
    {
        StubHttpMessageHandler handler = new(BuildAnthropicResponse(ChiaSeedsSuggestion));

        MealAdditionSuggestionResult result = await CreateService(handler).SuggestAdditionsAsync(BuildRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.That(result.Suggestions, Is.Empty);
        Assert.That(result.Errors, Has.Count.EqualTo(1));
        Assert.That(handler.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task SuggestAdditionsAsync_WithEmptySuggestions_ReturnsAndSavesAnEmptyList()
    {
        Guid mealId = await SeedMealAsync();
        StubHttpMessageHandler handler = new(BuildAnthropicResponse("[]"));

        MealAdditionSuggestionResult first = await CreateService(handler).SuggestAdditionsAsync(BuildRequest(mealId), CancellationToken.None);
        MealAdditionSuggestionResult second = await CreateService(handler).SuggestAdditionsAsync(BuildRequest(mealId), CancellationToken.None);

        Assert.That(first.Suggestions, Is.Empty);
        Assert.That(first.Errors, Is.Empty);
        Assert.That(second.Suggestions, Is.Empty);
        Assert.That(handler.CallCount, Is.EqualTo(1), "An empty answer is still an answer, so it shouldn't be re-requested.");
    }

    [Test]
    public async Task SuggestAdditionsAsync_DropsSuggestionsWithNoNameOrNonPositiveQuantity()
    {
        Guid mealId = await SeedMealAsync();
        string suggestions = """
            [
              {"ingredient":"Chia seeds","quantity":1,"unit":"tbsp","reason":"Adds fibre."},
              {"ingredient":"Salt","quantity":0,"unit":null,"reason":"To taste."},
              {"ingredient":" ","quantity":10,"unit":"g","reason":"Nameless."}
            ]
            """;

        MealAdditionSuggestionResult result = await CreateService(new StubHttpMessageHandler(BuildAnthropicResponse(suggestions)))
            .SuggestAdditionsAsync(BuildRequest(mealId), CancellationToken.None);

        Assert.That(result.Suggestions.Select(s => s.Ingredient), Is.EqualTo(new[] { "Chia seeds" }));
        Assert.That(GetSavedAdditions(mealId).Select(a => a.Name), Is.EqualTo(new[] { "Chia seeds" }));
    }

    [Test]
    public async Task SuggestAdditionsAsync_CalledTwiceForSameMeal_OnlyCallsAnthropicOnce()
    {
        Guid mealId = await SeedMealAsync();
        StubHttpMessageHandler handler = new(BuildAnthropicResponse(ChiaSeedsSuggestion));

        // Each service gets its own AppDbContext, so this simulates two separate requests - e.g.
        // a page reload - against the same database.
        MealAdditionSuggestionResult first = await CreateService(handler)
            .SuggestAdditionsAsync(BuildRequest(mealId), CancellationToken.None);
        MealAdditionSuggestionResult second = await CreateService(handler)
            .SuggestAdditionsAsync(BuildRequest(mealId), CancellationToken.None);

        Assert.That(handler.CallCount, Is.EqualTo(1));
        Assert.That(second.Errors, Is.Empty);
        Assert.That(second.Suggestions, Is.EqualTo(first.Suggestions));
    }

    [Test]
    public async Task SuggestAdditionsAsync_SameMealWithDifferentDayShortfall_ReusesSavedSuggestions()
    {
        Guid mealId = await SeedMealAsync();
        StubHttpMessageHandler handler = new(BuildAnthropicResponse(ChiaSeedsSuggestion));

        await CreateService(handler).SuggestAdditionsAsync(BuildRequest(mealId, fibreShortfallG: 8), CancellationToken.None);
        MealAdditionSuggestionResult second = await CreateService(handler)
            .SuggestAdditionsAsync(BuildRequest(mealId, fibreShortfallG: 12, plantsShortfall: 5), CancellationToken.None);

        Assert.That(handler.CallCount, Is.EqualTo(1));
        Assert.That(second.Suggestions.Select(s => s.Ingredient), Is.EqualTo(new[] { "Chia seeds" }));
    }

    [Test]
    public async Task SuggestAdditionsAsync_WhenMealIngredientsChange_RegeneratesAndReplacesOldSuggestions()
    {
        Guid mealId = await SeedMealAsync();
        SequencedHttpMessageHandler handler = new(
            OkResponse(ChiaSeedsSuggestion),
            OkResponse("""[{"ingredient":"Flaxseed","quantity":2,"unit":"tbsp","reason":"Adds fibre."}]"""));

        await CreateService(handler).SuggestAdditionsAsync(BuildRequest(mealId), CancellationToken.None);

        using (AppDbContext db = _testDatabase.CreateContext())
        {
            db.MealIngredients.Add(new MealIngredient { Id = Guid.NewGuid(), MealId = mealId, Name = "Banana", Quantity = 1m });
            await db.SaveChangesAsync();
        }

        MealAdditionSuggestionResult second = await CreateService(handler).SuggestAdditionsAsync(BuildRequest(mealId), CancellationToken.None);

        Assert.That(handler.CallCount, Is.EqualTo(2));
        Assert.That(second.Suggestions.Select(s => s.Ingredient), Is.EqualTo(new[] { "Flaxseed" }));
        Assert.That(GetSavedAdditions(mealId).Select(a => a.Name), Is.EqualTo(new[] { "Flaxseed" }));
    }

    [Test]
    public async Task SuggestAdditionsAsync_WhenMealNotesChange_Regenerates()
    {
        Guid mealId = await SeedMealAsync();
        StubHttpMessageHandler handler = new(BuildAnthropicResponse(ChiaSeedsSuggestion));

        await CreateService(handler).SuggestAdditionsAsync(BuildRequest(mealId), CancellationToken.None);

        using (AppDbContext db = _testDatabase.CreateContext())
        {
            MealEntry meal = db.Meals.Single(m => m.Id == mealId);
            meal.Notes = "Now with extra cinnamon";
            await db.SaveChangesAsync();
        }

        await CreateService(handler).SuggestAdditionsAsync(BuildRequest(mealId), CancellationToken.None);

        Assert.That(handler.CallCount, Is.EqualTo(2));
    }

    [Test]
    public async Task SuggestAdditionsAsync_AfterAnthropicCallFails_SavesNothingAndRetriesOnNextRequest()
    {
        Guid mealId = await SeedMealAsync();
        SequencedHttpMessageHandler handler = new(
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") },
            OkResponse(ChiaSeedsSuggestion));

        MealAdditionSuggestionResult first = await CreateService(handler).SuggestAdditionsAsync(BuildRequest(mealId), CancellationToken.None);

        Assert.That(first.Suggestions, Is.Empty);
        Assert.That(first.Errors, Has.Count.EqualTo(1));
        using (AppDbContext db = _testDatabase.CreateContext())
        {
            Assert.That(db.MealAdditionSuggestionSets.Count(), Is.EqualTo(0), "Errors are shown in the moment, never saved.");
        }

        MealAdditionSuggestionResult second = await CreateService(handler).SuggestAdditionsAsync(BuildRequest(mealId), CancellationToken.None);

        Assert.That(handler.CallCount, Is.EqualTo(2));
        Assert.That(second.Errors, Is.Empty);
        Assert.That(second.Suggestions, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SuggestAdditionsAsync_WithMalformedResponse_ReturnsErrorAndKeepsPreviousSuggestions()
    {
        Guid mealId = await SeedMealAsync();
        SequencedHttpMessageHandler handler = new(OkResponse(ChiaSeedsSuggestion), OkResponse("not json"));

        await CreateService(handler).SuggestAdditionsAsync(BuildRequest(mealId), CancellationToken.None);
        using (AppDbContext db = _testDatabase.CreateContext())
        {
            db.Meals.Single(m => m.Id == mealId).Notes = "Changed";
            await db.SaveChangesAsync();
        }

        MealAdditionSuggestionResult second = await CreateService(handler).SuggestAdditionsAsync(BuildRequest(mealId), CancellationToken.None);

        Assert.That(second.Errors, Has.Count.EqualTo(1));
        Assert.That(GetSavedAdditions(mealId).Select(a => a.Name), Is.EqualTo(new[] { "Chia seeds" }),
            "A failed regeneration shouldn't wipe the last good suggestions.");
    }

    [Test]
    public async Task DeletingMeal_AlsoDeletesItsSavedSuggestions()
    {
        Guid mealId = await SeedMealAsync();
        await CreateService(new StubHttpMessageHandler(BuildAnthropicResponse(ChiaSeedsSuggestion)))
            .SuggestAdditionsAsync(BuildRequest(mealId), CancellationToken.None);

        using (AppDbContext db = _testDatabase.CreateContext())
        {
            db.Meals.Remove(db.Meals.Single(m => m.Id == mealId));
            await db.SaveChangesAsync();
        }

        using AppDbContext readDb = _testDatabase.CreateContext();
        Assert.That(readDb.MealAdditionSuggestionSets.Count(), Is.EqualTo(0));
        Assert.That(readDb.MealSuggestedAdditions.Count(), Is.EqualTo(0));
    }

    private static Func<HttpResponseMessage> OkResponse(string suggestionsJsonArray)
        => () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(BuildAnthropicResponse(suggestionsJsonArray), Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public int CallCount { get; private set; }
        public string? LastRequestBody { get; private set; }

        public StubHttpMessageHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
            return response;
        }
    }

    /// <summary>Returns a different stubbed response on each successive call, so tests can simulate a failure followed by a retry succeeding.</summary>
    private sealed class SequencedHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage>[] _responses;

        public int CallCount { get; private set; }

        public SequencedHttpMessageHandler(params Func<HttpResponseMessage>[] responses)
        {
            _responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int index = Math.Min(CallCount, _responses.Length - 1);
            CallCount++;
            return Task.FromResult(_responses[index]());
        }
    }
}
