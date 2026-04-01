using CAP.Avalonia.ViewModels.AI;

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
    /// Sends a message to the AI with tool support. Handles the full tool-calling loop:
    /// Claude may call tools multiple times before returning the final text response.
    /// </summary>
    /// <param name="userMessage">The user's message to send.</param>
    /// <param name="history">Prior text-only conversation turns as (role, content) pairs.</param>
    /// <param name="tools">Tool definitions available for Claude to call.</param>
    /// <param name="executeToolAsync">Callback to execute a tool: (toolName, inputJson) → result string.</param>
    /// <param name="ct">Cancellation token to abort the request.</param>
    /// <returns>Final text response after all tool calls are resolved.</returns>
    Task<string> SendMessageWithToolsAsync(
        string userMessage,
        IReadOnlyList<(string Role, string Content)> history,
        IReadOnlyList<AiToolDefinition> tools,
        Func<string, string, Task<string>> executeToolAsync,
        CancellationToken ct = default);
}
