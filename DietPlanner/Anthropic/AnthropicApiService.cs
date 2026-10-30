using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace DietPlanner.Anthropic;

public sealed class AnthropicApiService : IAnthropicApiService
{
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _httpClient;
    private readonly AnthropicOptions _options;

    public AnthropicApiService(HttpClient httpClient, IOptions<AnthropicOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<T?> SendForJsonAsync<T>(AnthropicPrompt prompt, string userContent, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new AnthropicCallFailedException("Anthropic API key is not configured.");
        }

        var requestBody = new AnthropicRequest(_options.Model, prompt.MaxTokens, prompt.SystemPrompt, [new AnthropicMessage("user", userContent)]);

        using HttpRequestMessage request = new(HttpMethod.Post, _options.BaseUrl);
        request.Headers.Add("x-api-key", _options.ApiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        string responseBody;
        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new AnthropicCallFailedException(
                    $"The Anthropic API returned an error ({(int)response.StatusCode}).",
                    new HttpRequestException($"Anthropic API returned {(int)response.StatusCode}: {responseBody}"));
            }
        }
        catch (HttpRequestException ex)
        {
            throw new AnthropicCallFailedException("Could not reach the Anthropic API.", ex);
        }

        try
        {
            AnthropicResponse? anthropicResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseBody);
            string? text = anthropicResponse?.Content?.FirstOrDefault(c => c.Type == "text")?.Text;

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new AnthropicCallFailedException("The Anthropic API returned an empty reply.");
            }

            if (anthropicResponse?.StopReason == "max_tokens")
            {
                throw new AnthropicCallFailedException(prompt.TruncatedResponseMessage);
            }

            return JsonSerializer.Deserialize<T>(StripMarkdownFences(text));
        }
        catch (JsonException ex)
        {
            throw new AnthropicCallFailedException("The Anthropic API returned a reply that could not be read.", ex);
        }
    }

    private static string StripMarkdownFences(string text)
    {
        string trimmed = text.Trim();
        if (!trimmed.StartsWith("```"))
        {
            return trimmed;
        }

        int firstNewLine = trimmed.IndexOf('\n');
        int fenceEnd = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewLine < 0 || fenceEnd <= firstNewLine
            ? trimmed
            : trimmed[(firstNewLine + 1)..fenceEnd].Trim();
    }

    private sealed record AnthropicRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("system")] string System,
        [property: JsonPropertyName("messages")] List<AnthropicMessage> Messages);

    private sealed record AnthropicMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record AnthropicResponse(
        [property: JsonPropertyName("content")] List<AnthropicContentBlock> Content,
        [property: JsonPropertyName("stop_reason")] string? StopReason);

    private sealed record AnthropicContentBlock(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string? Text);
}
