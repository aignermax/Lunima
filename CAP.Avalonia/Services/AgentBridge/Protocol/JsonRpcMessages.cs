using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CAP.Avalonia.Services.AgentBridge.Protocol;

/// <summary>Standard JSON-RPC 2.0 error codes used by the agent bridge.</summary>
public static class JsonRpcErrorCodes
{
    /// <summary>Invalid JSON was received.</summary>
    public const int ParseError = -32700;

    /// <summary>The JSON sent is not a valid request object.</summary>
    public const int InvalidRequest = -32600;

    /// <summary>The method does not exist.</summary>
    public const int MethodNotFound = -32601;

    /// <summary>Invalid method parameters (MCP also uses it for an unknown tool name).</summary>
    public const int InvalidParams = -32602;

    /// <summary>Internal server error.</summary>
    public const int InternalError = -32603;
}

/// <summary>
/// Builds serialized JSON-RPC 2.0 response envelopes.
/// </summary>
public static class JsonRpcMessages
{
    private const string Version = "2.0";

    // Responses go to agents, never into HTML: escaping quotes inside the embedded JSON tool
    // results as \u0022 only triples their size in the agent's context.
    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>A success response carrying <paramref name="result"/>.</summary>
    public static string Result(JsonNode? id, JsonNode result) =>
        new JsonObject
        {
            ["jsonrpc"] = Version,
            ["id"] = id?.DeepClone(),
            ["result"] = result,
        }.ToJsonString(Compact);

    /// <summary>An error response (<paramref name="id"/> is null when the request id is unknown).</summary>
    public static string Error(JsonNode? id, int code, string message) =>
        new JsonObject
        {
            ["jsonrpc"] = Version,
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        }.ToJsonString(Compact);

    /// <summary>Reads the request id (string or number) as a node, or null.</summary>
    public static JsonNode? ReadId(JsonElement message) =>
        message.TryGetProperty("id", out var id) && id.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? JsonNode.Parse(id.GetRawText())
            : null;
}
