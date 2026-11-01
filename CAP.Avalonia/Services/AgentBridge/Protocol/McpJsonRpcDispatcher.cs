using System.Text.Json;
using System.Text.Json.Nodes;
using CAP.Avalonia.Services.AgentBridge.Tools;

namespace CAP.Avalonia.Services.AgentBridge.Protocol;

/// <summary>
/// Handles the MCP JSON-RPC methods the agent bridge supports: <c>initialize</c>,
/// <c>ping</c>, <c>tools/list</c> and <c>tools/call</c>. Notifications (no <c>id</c>) and
/// client responses are accepted silently. Transport-independent, so it is unit-testable
/// without a socket.
/// </summary>
public sealed class McpJsonRpcDispatcher
{
    /// <summary>Starts a fresh tool-admission generation.</summary>
    public void OpenAdmission() => _catalog.OpenAdmission();

    /// <summary>Closes tool admission and awaits accepted calls.</summary>
    public Task CloseAdmissionAsync() => _catalog.CloseAdmissionAsync();

    /// <summary>Cancellation callback diagnostics from the drained tool generation.</summary>
    public AggregateException? CancellationError => _catalog.CancellationError;

    /// <summary>Protocol revisions this server speaks, newest first.</summary>
    public static readonly IReadOnlyList<string> SupportedProtocolVersions =
        new[] { "2025-06-18", "2025-03-26", "2024-11-05" };

    /// <summary>Server name reported in <c>initialize</c>.</summary>
    public const string ServerName = "lunima";

    private const string ToolOverview =
        "Units: µm, degrees counter-clockwise, positions are a component's top-left corner, powers linear (1 = unit laser). " +
        "Build: new_design/open_design, list_component_types + describe_component_type, then apply_design (whole circuit " +
        "as JSON or gdsfactory YAML) or place_component (x/y, or attach_to another part's pin) and create_connection / " +
        "connect_bundle. Light: set_light_source (laser on = input, off = output). Verify: simulate, sweep_parameter, " +
        "sweep_spectrum, optimize, validate_design, check_foundry_drc, compare_netlist, screenshot. Repair: " +
        "list_connections blocked_only=true, suggest_fixes, apply_suggestion. Exchange: export_gds, export_netlist, " +
        "analyze_gds, import_gds. Logic/CPU: create_group, make_gate, save_as_prefab, logic_build, logic_truth_table, " +
        "logic_step, isa_run. Every edit is undoable (undo/redo); batch runs steps atomically. Connections can be named " +
        "by an endpoint 'component.pin'.";

    /// <summary>Server instructions when the bridge runs inside the interactive desktop app.</summary>
    public const string DesktopInstructions =
        "Lunima agent bridge: inspect and edit the photonic design open in the running Lunima app while the user " +
        "watches; the user can undo any edit with Ctrl+Z. Start with get_design_summary. " + ToolOverview;

    /// <summary>Server instructions when Lunima runs headless (stdio MCP server or CLI).</summary>
    public const string HeadlessInstructions =
        "Lunima headless engine: the full photonic chip design application without a window; one design per session. " +
        ToolOverview;

    private static readonly JsonElement EmptyArguments = JsonDocument.Parse("{}").RootElement.Clone();

    private readonly AgentToolCatalog _catalog;
    private readonly string _serverVersion;
    private readonly string _instructions;

    /// <summary>Creates the dispatcher over <paramref name="catalog"/>.</summary>
    public McpJsonRpcDispatcher(AgentToolCatalog catalog, string serverVersion, string instructions = DesktopInstructions)
    {
        _catalog = catalog;
        _serverVersion = serverVersion;
        _instructions = instructions;
    }

    /// <summary>
    /// Handles one JSON-RPC message. Returns the serialized response, or null when the
    /// message is a notification or a response that needs no answer.
    /// </summary>
    public async Task<string?> DispatchAsync(JsonElement message, CancellationToken token)
    {
        if (message.ValueKind != JsonValueKind.Object)
            return JsonRpcMessages.Error(null, JsonRpcErrorCodes.InvalidRequest, "Invalid Request");

        var id = JsonRpcMessages.ReadId(message);
        var hasId = message.TryGetProperty("id", out _);
        if (!message.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
            return hasId && !IsClientResponse(message)
                ? JsonRpcMessages.Error(id, JsonRpcErrorCodes.InvalidRequest, "Invalid Request")
                : null;
        if (!hasId)
            return null;

        var parameters = message.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object
            ? p
            : EmptyArguments;
        return await DispatchMethodAsync(id, methodElement.GetString()!, parameters, token);
    }

    private async Task<string> DispatchMethodAsync(
        JsonNode? id, string method, JsonElement parameters, CancellationToken token) => method switch
    {
        "initialize" => JsonRpcMessages.Result(id, BuildInitializeResult(parameters)),
        "ping" => JsonRpcMessages.Result(id, new JsonObject()),
        "tools/list" => JsonRpcMessages.Result(id, BuildToolList()),
        "tools/call" => await CallToolAsync(id, parameters, token),
        _ => JsonRpcMessages.Error(id, JsonRpcErrorCodes.MethodNotFound, $"Method not found: {method}"),
    };

    private static bool IsClientResponse(JsonElement message) =>
        message.TryGetProperty("result", out _) || message.TryGetProperty("error", out _);

    private JsonObject BuildInitializeResult(JsonElement parameters)
    {
        var requested = parameters.TryGetProperty("protocolVersion", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
        var negotiated = requested != null && SupportedProtocolVersions.Contains(requested)
            ? requested
            : SupportedProtocolVersions[0];

        return new JsonObject
        {
            ["protocolVersion"] = negotiated,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = ServerName,
                ["title"] = "Lunima",
                ["version"] = _serverVersion,
            },
            ["instructions"] = _instructions,
        };
    }

    private JsonObject BuildToolList()
    {
        var tools = new JsonArray();
        foreach (var tool in _catalog.Tools)
        {
            tools.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = JsonSerializer.SerializeToNode(tool.InputSchema),
            });
        }
        return new JsonObject { ["tools"] = tools };
    }

    private async Task<string> CallToolAsync(JsonNode? id, JsonElement parameters, CancellationToken token)
    {
        if (!parameters.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
            return JsonRpcMessages.Error(id, JsonRpcErrorCodes.InvalidParams, "tools/call requires a tool name");

        var name = nameElement.GetString()!;
        if (!_catalog.Contains(name))
            return JsonRpcMessages.Error(id, JsonRpcErrorCodes.InvalidParams, $"Unknown tool: {name}");

        var arguments = parameters.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object
            ? a
            : EmptyArguments;
        token.ThrowIfCancellationRequested();
        var result = await _catalog.InvokeAsync(name, arguments, token);
        return JsonRpcMessages.Result(id, ToJson(result));
    }

    private static JsonObject ToJson(AgentToolResult result)
    {
        var content = new JsonArray();
        foreach (var block in result.Content)
        {
            var node = new JsonObject { ["type"] = block.Type };
            if (block.Text != null) node["text"] = block.Text;
            if (block.Data != null) node["data"] = block.Data;
            if (block.MimeType != null) node["mimeType"] = block.MimeType;
            content.Add(node);
        }
        return new JsonObject { ["content"] = content, ["isError"] = result.IsError };
    }
}
