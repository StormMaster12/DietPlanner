namespace DietPlanner.Endpoints.Meal;

/// <summary>
/// Configuration for calling the Anthropic Messages API to extract structured meal data from
/// free-form PDF text. Bound from the "Anthropic" configuration section - the API key should be
/// supplied via user secrets or an environment variable (ANTHROPIC__APIKEY), never committed.
/// </summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-4-6";
}
