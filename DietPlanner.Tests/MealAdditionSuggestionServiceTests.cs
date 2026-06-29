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
    private static MealAdditionSuggestionService CreateService(string anthropicResponseBody, string apiKey = "test-key")
    {
        StubHttpMessageHandler handler = new(anthropicResponseBody);
        HttpClient httpClient = new(handler);
        IOptions<AnthropicOptions> options = Options.Create(new AnthropicOptions { ApiKey = apiKey, Model = "claude-sonnet-4-6" });
        return new MealAdditionSuggestionService(httpClient, options, NullLogger<MealAdditionSuggestionService>.Instance);
    }

    private static string BuildAnthropicResponse(string suggestionsJsonArray)
        => JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = suggestionsJsonArray } } });

    private static MealAdditionSuggestionRequest BuildRequest()
        => new(
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
        MealAdditionSuggestionService service = CreateService(BuildAnthropicResponse(suggestionsJsonArray));

        MealAdditionSuggestionResult result = await service.SuggestAdditionsAsync(BuildRequest(), CancellationToken.None);

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Suggestions, Has.Count.EqualTo(1));
        Assert.That(result.Suggestions[0].Ingredient, Is.EqualTo("Chia seeds"));
        Assert.That(result.Suggestions[0].Amount, Is.EqualTo("1 tbsp"));
    }

    [Test]
    public async Task SuggestAdditionsAsync_WithNoApiKeyConfigured_ReturnsConfigurationError()
    {
        MealAdditionSuggestionService service = CreateService(BuildAnthropicResponse("[]"), apiKey: "");

        MealAdditionSuggestionResult result = await service.SuggestAdditionsAsync(BuildRequest(), CancellationToken.None);

        Assert.That(result.Suggestions, Is.Empty);
        Assert.That(result.Errors.Count(e => e.Contains("not configured")), Is.EqualTo(1));
    }

    [Test]
    public async Task SuggestAdditionsAsync_WithEmptySuggestions_ReturnsEmptyList()
    {
        MealAdditionSuggestionService service = CreateService(BuildAnthropicResponse("[]"));

        MealAdditionSuggestionResult result = await service.SuggestAdditionsAsync(BuildRequest(), CancellationToken.None);

        Assert.That(result.Suggestions, Is.Empty);
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
