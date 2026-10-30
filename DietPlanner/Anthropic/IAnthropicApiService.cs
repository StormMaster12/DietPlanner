namespace DietPlanner.Anthropic;

/// <summary>
/// Single shared entry point for calling the Anthropic Messages API. Every feature that needs an
/// LLM call (PDF import, ingredient normalization/grouping, meal addition suggestions) goes through
/// this service so the request shape, headers, and response handling live in exactly one place.
/// </summary>
public interface IAnthropicApiService
{
    /// <summary>
    /// Whether an API key is configured. Callers should check this before calling
    /// <see cref="SendForJsonAsync{T}"/> so they can surface a feature-specific "not configured"
    /// message instead of a generic failure.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Sends a single-turn message to the Anthropic Messages API and deserializes the reply (with any
    /// markdown code fences stripped) as JSON.
    /// </summary>
    /// <exception cref="AnthropicCallFailedException">
    /// No API key is configured, the API could not be reached or returned an error, or the reply was
    /// empty, truncated, or not valid JSON for <typeparamref name="T"/>.
    /// </exception>
    Task<T?> SendForJsonAsync<T>(AnthropicPrompt prompt, string userContent, CancellationToken cancellationToken);
}
