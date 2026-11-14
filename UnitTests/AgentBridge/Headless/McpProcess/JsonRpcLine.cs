using System.Text.Json;

namespace UnitTests.AgentBridge.Headless.McpProcess;

/// <summary>
/// MCP stdio framing: every line on the server's stdout must be one complete JSON-RPC 2.0 message.
/// A single log line there (the classic MCP failure) makes clients drop the server.
/// </summary>
internal static class JsonRpcLine
{
    private const string ProtocolVersion = "2.0";

    /// <summary>Why <paramref name="line"/> is not a JSON-RPC 2.0 message, or null when it is one.</summary>
    public static string? Violation(string line)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(line); }
        catch (JsonException) { return "not JSON"; }

        using (document)
        {
            var message = document.RootElement;
            if (message.ValueKind != JsonValueKind.Object)
                return "not a JSON object";
            if (!message.TryGetProperty("jsonrpc", out var version) || version.ValueKind != JsonValueKind.String
                || version.GetString() != ProtocolVersion)
                return "no \"jsonrpc\": \"2.0\"";
            return IsResponse(message) || IsRequestOrNotification(message)
                ? null
                : "neither a response (id with result or error) nor a request/notification (method)";
        }
    }

    private static bool IsResponse(JsonElement message) =>
        message.TryGetProperty("id", out _)
        && message.TryGetProperty("result", out _) != message.TryGetProperty("error", out _);

    private static bool IsRequestOrNotification(JsonElement message) =>
        message.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String;
}
