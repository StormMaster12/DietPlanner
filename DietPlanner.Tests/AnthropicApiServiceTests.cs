using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace DietPlanner.Tests;

[TestFixture]
public sealed class AnthropicApiServiceTests
{
    private static readonly AnthropicPrompt Prompt = new("system", MaxTokens: 100, TruncatedResponseMessage: "Too long - try less.");

    private static AnthropicApiService CreateService(HttpStatusCode statusCode, string responseBody)
    {
        HttpClient httpClient = new(new StubHttpMessageHandler(statusCode, responseBody));
        IOptions<AnthropicOptions> options = Options.Create(new AnthropicOptions { ApiKey = "test-key" });
        return new AnthropicApiService(httpClient, options);
    }

    private static string BuildAnthropicResponse(string text, string? stopReason = null)
        => JsonSerializer.Serialize(new { content = new[] { new { type = "text", text } }, stop_reason = stopReason });

    [Test]
    public async Task SendForJsonAsync_DeserializesTheReplyWithMarkdownFencesStripped()
    {
        AnthropicApiService service = CreateService(HttpStatusCode.OK, BuildAnthropicResponse("```json\n[1, 2]\n```"));

        List<int>? result = await service.SendForJsonAsync<List<int>>(Prompt, "user", CancellationToken.None);

        Assert.That(result, Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void SendForJsonAsync_OnErrorStatus_ReportsTheStatusButNotTheResponseBody()
    {
        AnthropicApiService service = CreateService(HttpStatusCode.InternalServerError, "secret upstream detail");

        AnthropicCallFailedException? ex = Assert.ThrowsAsync<AnthropicCallFailedException>(
            async () => await service.SendForJsonAsync<List<int>>(Prompt, "user", CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("500"));
        Assert.That(ex.Message, Does.Not.Contain("secret upstream detail"));
        Assert.That(ex.InnerException?.Message, Does.Contain("secret upstream detail"), "The detail is kept for logging.");
    }

    [Test]
    public void SendForJsonAsync_OnTruncatedReply_UsesThePromptsTruncationMessage()
    {
        AnthropicApiService service = CreateService(HttpStatusCode.OK, BuildAnthropicResponse("[1,", stopReason: "max_tokens"));

        AnthropicCallFailedException? ex = Assert.ThrowsAsync<AnthropicCallFailedException>(
            async () => await service.SendForJsonAsync<List<int>>(Prompt, "user", CancellationToken.None));

        Assert.That(ex!.Message, Is.EqualTo("Too long - try less."));
    }

    [Test]
    public void SendForJsonAsync_OnMalformedJson_ReportsAReadableMessageNotTheParserError()
    {
        AnthropicApiService service = CreateService(HttpStatusCode.OK, BuildAnthropicResponse("not json"));

        AnthropicCallFailedException? ex = Assert.ThrowsAsync<AnthropicCallFailedException>(
            async () => await service.SendForJsonAsync<List<int>>(Prompt, "user", CancellationToken.None));

        Assert.That(ex!.Message, Is.EqualTo("The Anthropic API returned a reply that could not be read."));
        Assert.That(ex.InnerException, Is.InstanceOf<JsonException>());
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public StubHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            });
    }
}
