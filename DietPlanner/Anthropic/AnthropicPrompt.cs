namespace DietPlanner.Anthropic;

/// <param name="SystemPrompt">Instructions telling the model what JSON shape to reply with.</param>
/// <param name="MaxTokens">Upper bound on the reply length.</param>
/// <param name="TruncatedResponseMessage">
/// Shown to the user if the reply hits <paramref name="MaxTokens"/> - each feature knows best how
/// to advise working around that for its own request shape (e.g. "split the PDF into smaller files").
/// </param>
public sealed record AnthropicPrompt(string SystemPrompt, int MaxTokens, string TruncatedResponseMessage);
