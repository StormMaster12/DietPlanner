namespace DietPlanner.Anthropic;

/// <summary>
/// Thrown by <see cref="IAnthropicApiService"/> for any failed call. <see cref="Exception.Message"/>
/// is always safe to show the user; the raw HTTP/JSON failure (which can echo the API's response
/// body) is kept only on <see cref="Exception.InnerException"/> for logging.
/// </summary>
public sealed class AnthropicCallFailedException : Exception
{
    public AnthropicCallFailedException(string userMessage, Exception? innerException = null)
        : base(userMessage, innerException)
    {
    }
}
