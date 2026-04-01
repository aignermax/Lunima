namespace CAP.Avalonia.Services;

/// <summary>
/// Contract for AI service integration (supports Claude, OpenAI, etc.).
/// </summary>
public interface IAiService
{
    /// <summary>
    /// Returns true when an API key has been configured and the service is ready to use.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Configures the API key for authentication.
    /// </summary>
    void SetApiKey(string apiKey);

    /// <summary>
    /// Sends a message to the AI and returns the response text.
    /// </summary>
    /// <param name="userMessage">The user's message to send.</param>
    /// <param name="history">Prior conversation turns as (role, content) pairs.</param>
    /// <param name="ct">Cancellation token to abort the request.</param>
    Task<string> SendMessageAsync(
        string userMessage,
        IReadOnlyList<(string Role, string Content)> history,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a message to the AI with tool-calling (function calling) support.
    /// Handles the full tool-use loop until a final text response is produced.
    /// </summary>
    /// <param name="userMessage">The user's message.</param>
    /// <param name="history">Prior conversation turns as (role, content) pairs.</param>
    /// <param name="availableComponentTypes">Component type names to include in the place_component tool description.</param>
    /// <param name="toolExecutor">Delegate invoked for each tool call: (toolName, toolInputJson) → result string.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string> SendMessageWithToolsAsync(
        string userMessage,
        IReadOnlyList<(string Role, string Content)> history,
        IReadOnlyList<string> availableComponentTypes,
        Func<string, string, Task<string>> toolExecutor,
        CancellationToken ct = default);
}
