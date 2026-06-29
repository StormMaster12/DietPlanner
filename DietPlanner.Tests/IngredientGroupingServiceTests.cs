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
    private static IngredientGroupingService CreateService(string anthropicResponseBody, string apiKey = "test-key")
    {
        StubHttpMessageHandler handler = new(anthropicResponseBody);
        HttpClient httpClient = new(handler);
        IOptions<AnthropicOptions> options = Options.Create(new AnthropicOptions { ApiKey = apiKey, Model = "claude-sonnet-4-6" });
        return new IngredientGroupingService(httpClient, options, NullLogger<IngredientGroupingService>.Instance);
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
        IngredientGroupingService service = CreateService(BuildAnthropicResponse(groupedJsonArray));

        IngredientGroupingResult result = await service.GroupIngredientsAsync(ingredients, CancellationToken.None);

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
        IngredientGroupingService service = CreateService(BuildAnthropicResponse(groupedJsonArray));

        IngredientGroupingResult result = await service.GroupIngredientsAsync(ingredients, CancellationToken.None);

        Assert.That(result.Ingredients.Single().Category, Is.EqualTo("Other"));
    }

    [Test]
    public async Task GroupIngredientsAsync_WithNoApiKeyConfigured_ReturnsConfigurationError()
    {
        List<DayPlanIngredientDto> ingredients = [new("Chicken breast", 1m, null)];
        IngredientGroupingService service = CreateService(BuildAnthropicResponse("[]"), apiKey: "");

        IngredientGroupingResult result = await service.GroupIngredientsAsync(ingredients, CancellationToken.None);

        Assert.That(result.Ingredients, Is.Empty);
        Assert.That(result.Errors.Count(e => e.Contains("not configured")), Is.EqualTo(1));
    }

    [Test]
    public async Task GroupIngredientsAsync_WithNoIngredients_ReturnsEmptyResult()
    {
        IngredientGroupingService service = CreateService(BuildAnthropicResponse("[]"));

        IngredientGroupingResult result = await service.GroupIngredientsAsync([], CancellationToken.None);

        Assert.That(result.Ingredients, Is.Empty);
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
