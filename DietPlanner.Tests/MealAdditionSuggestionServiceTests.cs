using System.Net;
using System.Text;
using System.Text.Json;
using DietPlanner.Endpoints.DayPlan;
using DietPlanner.Endpoints.Meal;
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

    private static MealAdditionSuggestionRequest BuildRequest(Guid? mealId = null)
        => new(
            mealId ?? Guid.NewGuid(),
            "Porridge",
            "Good for gut health",
            "Make it creamy",
            [new DayPlanIngredientDto("Oats", 50m, "g")],
            8,
            2);

    [Test]
    public async Task SuggestAdditionsAsync_WithSuggestions_ReturnsThem()
    {
        string suggestionsJsonArray = """
            [{"ingredient":"Chia seeds","amount":"1 tbsp","reason":"Adds fibre without changing the flavour much."}]
            """;
        MealAdditionSuggestionService service = CreateService(new StubHttpMessageHandler(BuildAnthropicResponse(suggestionsJsonArray)));

        MealAdditionSuggestionResult result = await service.SuggestAdditionsAsync(BuildRequest(), CancellationToken.None);

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Suggestions, Has.Count.EqualTo(1));
        Assert.That(result.Suggestions[0].Ingredient, Is.EqualTo("Chia seeds"));
        Assert.That(result.Suggestions[0].Amount, Is.EqualTo("1 tbsp"));
    }

    [Test]
    public async Task SuggestAdditionsAsync_WithNoApiKeyConfigured_ReturnsConfigurationError()
    {
        MealAdditionSuggestionService service = CreateService(new StubHttpMessageHandler(BuildAnthropicResponse("[]")), apiKey: "");

        MealAdditionSuggestionResult result = await service.SuggestAdditionsAsync(BuildRequest(), CancellationToken.None);

        Assert.That(result.Suggestions, Is.Empty);
        Assert.That(result.Errors.Count(e => e.Contains("not configured")), Is.EqualTo(1));
    }

    [Test]
    public async Task SuggestAdditionsAsync_WithEmptySuggestions_ReturnsEmptyList()
    {
        MealAdditionSuggestionService service = CreateService(new StubHttpMessageHandler(BuildAnthropicResponse("[]")));

        MealAdditionSuggestionResult result = await service.SuggestAdditionsAsync(BuildRequest(), CancellationToken.None);

        Assert.That(result.Suggestions, Is.Empty);
        Assert.That(result.Errors, Is.Empty);
    }

    [Test]
    public async Task SuggestAdditionsAsync_CalledTwiceForSameMealAndInputs_OnlyCallsAnthropicOnce()
    {
        Guid mealId = Guid.NewGuid();
        string suggestionsJsonArray = """
            [{"ingredient":"Chia seeds","amount":"1 tbsp","reason":"Adds fibre without changing the flavour much."}]
            """;
        StubHttpMessageHandler handler = new(BuildAnthropicResponse(suggestionsJsonArray));

        // Persistence is per-instance state (AppDbContext), so simulate two separate requests -
        // e.g. a page reload - by creating a fresh service each time against the same database.
        MealAdditionSuggestionResult first = await CreateService(handler)
            .SuggestAdditionsAsync(BuildRequest(mealId), CancellationToken.None);
        MealAdditionSuggestionResult second = await CreateService(handler)
            .SuggestAdditionsAsync(BuildRequest(mealId), CancellationToken.None);

        Assert.That(handler.CallCount, Is.EqualTo(1));
        Assert.That(second.Suggestions, Has.Count.EqualTo(1));
        Assert.That(second.Suggestions[0].Ingredient, Is.EqualTo(first.Suggestions[0].Ingredient));
    }

    [Test]
    public async Task SuggestAdditionsAsync_WhenMealInputsChange_CallsAnthropicAgain()
    {
        Guid mealId = Guid.NewGuid();
        StubHttpMessageHandler handler = new(BuildAnthropicResponse("""
            [{"ingredient":"Chia seeds","amount":"1 tbsp","reason":"Adds fibre."}]
            """));

        await CreateService(handler).SuggestAdditionsAsync(BuildRequest(mealId) with { FibreShortfallG = 8 }, CancellationToken.None);
        await CreateService(handler).SuggestAdditionsAsync(BuildRequest(mealId) with { FibreShortfallG = 12 }, CancellationToken.None);

        Assert.That(handler.CallCount, Is.EqualTo(2));
    }

    [Test]
    public async Task SuggestAdditionsAsync_AfterAnthropicCallFails_RetriesOnNextRequest()
    {
        Guid mealId = Guid.NewGuid();
        SequencedHttpMessageHandler handler = new(
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") },
            () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    BuildAnthropicResponse("""[{"ingredient":"Chia seeds","amount":"1 tbsp","reason":"Adds fibre."}]"""),
                    Encoding.UTF8, "application/json")
            });

        MealAdditionSuggestionResult first = await CreateService(handler).SuggestAdditionsAsync(BuildRequest(mealId), CancellationToken.None);
        MealAdditionSuggestionResult second = await CreateService(handler).SuggestAdditionsAsync(BuildRequest(mealId), CancellationToken.None);

        Assert.That(first.Suggestions, Is.Empty);
        Assert.That(first.Errors, Has.Count.EqualTo(1));
        Assert.That(handler.CallCount, Is.EqualTo(2));
        Assert.That(second.Errors, Is.Empty);
        Assert.That(second.Suggestions, Has.Count.EqualTo(1));
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
