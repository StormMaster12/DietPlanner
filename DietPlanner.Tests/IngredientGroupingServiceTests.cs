using System.Net;
using System.Text;
using System.Text.Json;
using DietPlanner.Endpoints.DayPlan;
using DietPlanner.Endpoints.Meal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DietPlanner.Tests;

[TestFixture]
public sealed class IngredientGroupingServiceTests
{
    private static readonly DateOnly WeekStartDate = new(2026, 6, 29);

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

    private IngredientGroupingService CreateService(HttpMessageHandler handler, string apiKey = "test-key")
    {
        HttpClient httpClient = new(handler);
        IOptions<AnthropicOptions> options = Options.Create(new AnthropicOptions { ApiKey = apiKey, Model = "claude-sonnet-4-6" });
        AnthropicApiService anthropicApi = new(httpClient, options);
        return new IngredientGroupingService(anthropicApi, _testDatabase.CreateContext(), NullLogger<IngredientGroupingService>.Instance);
    }

    private static string BuildAnthropicResponse(string groupedJsonArray)
        => JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = groupedJsonArray } } });

    [Test]
    public async Task GroupIngredientsAsync_WithMixedUnits_ConvertsToGramsAndAssignsCategory()
    {
        List<DayPlanIngredientDto> ingredients =
        [
            new("0% Greek yogurt", 2.5m, "cup"),
            new("Apple", 12m, null),
        ];

        string groupedJsonArray = """
            [{"index":0,"grams":600,"category":"Dairy & Eggs"},{"index":1,"grams":2160,"category":"Produce"}]
            """;
        IngredientGroupingService service = CreateService(new StubHttpMessageHandler(BuildAnthropicResponse(groupedJsonArray)));

        IngredientGroupingResult result = await service.GroupIngredientsAsync(WeekStartDate, ingredients, CancellationToken.None);

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Ingredients, Has.Count.EqualTo(2));

        GroupedIngredientDto yogurt = result.Ingredients.Single(i => i.Name == "0% Greek yogurt");
        Assert.That(yogurt.Grams, Is.EqualTo(600));
        Assert.That(yogurt.Category, Is.EqualTo("Dairy & Eggs"));

        GroupedIngredientDto apple = result.Ingredients.Single(i => i.Name == "Apple");
        Assert.That(apple.Grams, Is.EqualTo(2160));
        Assert.That(apple.Category, Is.EqualTo("Produce"));
    }

    [Test]
    public async Task GroupIngredientsAsync_WithUnrecognizedCategory_FallsBackToOther()
    {
        List<DayPlanIngredientDto> ingredients = [new("Mystery item", 1m, null)];

        string groupedJsonArray = """[{"index":0,"grams":100,"category":"Not a real aisle"}]""";
        IngredientGroupingService service = CreateService(new StubHttpMessageHandler(BuildAnthropicResponse(groupedJsonArray)));

        IngredientGroupingResult result = await service.GroupIngredientsAsync(WeekStartDate, ingredients, CancellationToken.None);

        Assert.That(result.Ingredients.Single().Category, Is.EqualTo("Other"));
    }

    [Test]
    public async Task GroupIngredientsAsync_WithNoApiKeyConfigured_ReturnsConfigurationError()
    {
        List<DayPlanIngredientDto> ingredients = [new("Chicken breast", 1m, null)];
        IngredientGroupingService service = CreateService(new StubHttpMessageHandler(BuildAnthropicResponse("[]")), apiKey: "");

        IngredientGroupingResult result = await service.GroupIngredientsAsync(WeekStartDate, ingredients, CancellationToken.None);

        Assert.That(result.Ingredients, Is.Empty);
        Assert.That(result.Errors.Count(e => e.Contains("not configured")), Is.EqualTo(1));
    }

    [Test]
    public async Task GroupIngredientsAsync_WithNoIngredients_ReturnsEmptyResult()
    {
        IngredientGroupingService service = CreateService(new StubHttpMessageHandler(BuildAnthropicResponse("[]")));

        IngredientGroupingResult result = await service.GroupIngredientsAsync(WeekStartDate, [], CancellationToken.None);

        Assert.That(result.Ingredients, Is.Empty);
        Assert.That(result.Errors, Is.Empty);
    }

    [Test]
    public async Task GroupIngredientsAsync_CalledTwiceForSameWeekAndInputs_OnlyCallsAnthropicOnce()
    {
        List<DayPlanIngredientDto> ingredients = [new("Apple", 12m, null)];
        StubHttpMessageHandler handler = new(BuildAnthropicResponse("""[{"index":0,"grams":2160,"category":"Produce"}]"""));

        // Persistence is per-instance state (AppDbContext), so simulate two separate requests -
        // e.g. re-displaying the same week - by creating a fresh service each time against the
        // same database.
        IngredientGroupingResult first = await CreateService(handler)
            .GroupIngredientsAsync(WeekStartDate, ingredients, CancellationToken.None);
        IngredientGroupingResult second = await CreateService(handler)
            .GroupIngredientsAsync(WeekStartDate, ingredients, CancellationToken.None);

        Assert.That(handler.CallCount, Is.EqualTo(1));
        Assert.That(second.Ingredients.Single().Category, Is.EqualTo(first.Ingredients.Single().Category));
    }

    [Test]
    public async Task GroupIngredientsAsync_WhenIngredientsChange_CallsAnthropicAgain()
    {
        StubHttpMessageHandler handler = new(BuildAnthropicResponse("""[{"index":0,"grams":2160,"category":"Produce"}]"""));

        await CreateService(handler).GroupIngredientsAsync(WeekStartDate, [new("Apple", 12m, null)], CancellationToken.None);
        await CreateService(handler).GroupIngredientsAsync(WeekStartDate, [new("Apple", 20m, null)], CancellationToken.None);

        Assert.That(handler.CallCount, Is.EqualTo(2));
    }

    [Test]
    public async Task GroupIngredientsAsync_AfterAnthropicCallFails_RetriesOnNextRequest()
    {
        List<DayPlanIngredientDto> ingredients = [new("Apple", 12m, null)];
        SequencedHttpMessageHandler handler = new(
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") },
            () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    BuildAnthropicResponse("""[{"index":0,"grams":2160,"category":"Produce"}]"""),
                    Encoding.UTF8, "application/json")
            });

        IngredientGroupingResult first = await CreateService(handler).GroupIngredientsAsync(WeekStartDate, ingredients, CancellationToken.None);
        IngredientGroupingResult second = await CreateService(handler).GroupIngredientsAsync(WeekStartDate, ingredients, CancellationToken.None);

        Assert.That(first.Ingredients, Is.Empty);
        Assert.That(first.Errors, Has.Count.EqualTo(1));
        Assert.That(handler.CallCount, Is.EqualTo(2));
        Assert.That(second.Errors, Is.Empty);
        Assert.That(second.Ingredients.Single().Category, Is.EqualTo("Produce"));
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public int CallCount { get; private set; }

        public StubHttpMessageHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
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
