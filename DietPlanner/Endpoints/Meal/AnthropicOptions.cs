namespace DietPlanner.Endpoints.Meal;

/// <summary>
/// Configuration for <see cref="AnthropicApiService"/>, the single entry point every feature uses to
/// call the Anthropic Messages API (PDF import, ingredient normalization/grouping, meal addition
/// suggestions). Bound from the "Anthropic" configuration section - the API key should be supplied
/// via user secrets or an environment variable (ANTHROPIC__APIKEY), never committed.
/// </summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-4-6";
    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/messages";
}
