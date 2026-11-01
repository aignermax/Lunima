using System.Text.Encodings.Web;
using System.Text.Json;

namespace CAP.Avalonia.Services.AgentBridge.Tools;

/// <summary>One MCP content block of a tool result: text, or a base64 image.</summary>
/// <param name="Type"><c>text</c> or <c>image</c>.</param>
/// <param name="Text">The text for a text block.</param>
/// <param name="Data">Base64 payload for an image block.</param>
/// <param name="MimeType">MIME type for an image block (e.g. <c>image/png</c>).</param>
public sealed record AgentToolContent(string Type, string? Text = null, string? Data = null, string? MimeType = null);

/// <summary>
/// Result of an agent-bridge tool call. A failed call is reported as a normal result with
/// <see cref="IsError"/> set (MCP "tool execution error"), so the agent can read the reason
/// and correct itself.
/// </summary>
public sealed record AgentToolResult(IReadOnlyList<AgentToolContent> Content, bool IsError = false)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>A single text block.</summary>
    public static AgentToolResult Text(string text) => new(new[] { new AgentToolContent("text", text) });

    /// <summary>A single text block holding <paramref name="value"/> serialized as snake_case JSON.</summary>
    public static AgentToolResult Json(object value) => Text(JsonSerializer.Serialize(value, JsonOptions));

    /// <summary>A tool execution error with a human-readable reason.</summary>
    public static AgentToolResult Error(string message) =>
        new(new[] { new AgentToolContent("text", message) }, IsError: true);

    /// <summary>A PNG image followed by a short text caption.</summary>
    public static AgentToolResult Png(byte[] png, string caption) => new(new[]
    {
        new AgentToolContent("image", Data: Convert.ToBase64String(png), MimeType: "image/png"),
        new AgentToolContent("text", caption),
    });
}
