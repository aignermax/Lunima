namespace CAP.Avalonia.ViewModels.AI;

/// <summary>
/// Identifies the sender role of a chat message.
/// </summary>
public enum AiChatRole
{
    /// <summary>Message sent by the user.</summary>
    User,
    /// <summary>Response from the AI assistant.</summary>
    Assistant,
    /// <summary>System status or informational message.</summary>
    System
}

/// <summary>
/// Represents a single message in the AI assistant chat history.
/// Immutable record with computed display properties.
/// </summary>
public record AiChatMessage
{
    /// <summary>Gets the role of the message sender.</summary>
    public AiChatRole Role { get; init; }

    /// <summary>Gets the text content of the message.</summary>
    public string Content { get; init; } = "";

    /// <summary>Gets the timestamp when the message was created.</summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;

    /// <summary>Returns true if this message was sent by the user.</summary>
    public bool IsUser => Role == AiChatRole.User;

    /// <summary>
    /// Display label shown above the message ("You" / "AI" / "System").
    /// </summary>
    public string RoleLabel => Role switch
    {
        AiChatRole.User => "You",
        AiChatRole.Assistant => "AI",
        _ => "System"
    };

    /// <summary>
    /// Background color for the message bubble — distinguishes user vs AI messages.
    /// </summary>
    public string MessageBackground => IsUser ? "#1e2e3e" : "#1e1e2e";
}
