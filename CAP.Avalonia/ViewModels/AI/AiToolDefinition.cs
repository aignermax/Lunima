namespace CAP.Avalonia.ViewModels.AI;

/// <summary>
/// Defines a tool that can be called by the AI during a conversation.
/// Matches the Anthropic Claude API tool schema format.
/// </summary>
public record AiToolDefinition
{
    /// <summary>Tool name used by Claude to invoke it.</summary>
    public string Name { get; init; } = "";

    /// <summary>Description shown to Claude to guide when to use the tool.</summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// JSON Schema object describing the tool's input parameters.
    /// Must be serializable to the Anthropic API tool input_schema format.
    /// </summary>
    public object InputSchema { get; init; } = new { type = "object", properties = new { } };
}

/// <summary>
/// A tool invocation requested by the AI in a response.
/// </summary>
public record AiToolCall
{
    /// <summary>Unique ID for this tool call, used when sending back the result.</summary>
    public string Id { get; init; } = "";

    /// <summary>The name of the tool to execute.</summary>
    public string Name { get; init; } = "";

    /// <summary>JSON string containing the tool input parameters.</summary>
    public string InputJson { get; init; } = "{}";
}
