using System.Text.Json;

namespace DietPlanner.Endpoints.Meal;

/// <summary>
/// Single shared entry point for calling the Anthropic Messages API. Every feature that needs an
/// LLM call (PDF import, ingredient normalization/grouping, meal addition suggestions) goes through
/// this service so the request shape, headers, and response handling live in exactly one place.
/// </summary>
public interface IAnthropicApiService
{
    /// <summary>
    /// Whether an API key is configured. Callers should check this before calling
    /// <see cref="SendMessageAsync"/> so they can surface a feature-specific "not configured"
    /// message instead of a generic HTTP failure.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Sends a single-turn message to the Anthropic Messages API and returns the response text with
    /// any markdown code fences stripped, ready to deserialize as JSON.
    /// </summary>
    /// <param name="truncatedResponseMessage">
    /// Exception message to use if the response is truncated by <paramref name="maxTokens"/>
    /// (stop_reason "max_tokens") - callers know best how to advise the user to work around this
    /// for their specific request shape (e.g. "split the PDF into smaller files").
    /// </param>
    /// <exception cref="HttpRequestException">No API key is configured, or the API returned a non-success status code.</exception>
    /// <exception cref="JsonException">The response had no text content, or was truncated.</exception>
    Task<string> SendMessageAsync(
        string systemPrompt,
        string userContent,
        int maxTokens,
        string truncatedResponseMessage,
        CancellationToken cancellationToken);
}
