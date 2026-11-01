using System.Text.Json;
using System.Text.Json.Nodes;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Edit;

/// <summary>
/// <c>batch</c>: runs a list of tool calls in one request, in order. Atomic by default: when a step
/// fails, every undoable step the batch made is undone again, so the design is either fully
/// changed or not at all — the keep-or-revert loop agents use for optimisation.
/// </summary>
public sealed class BatchTool : IAgentBridgeTool
{
    private static readonly HashSet<string> NotBatchable = new(StringComparer.Ordinal)
    {
        "batch", "new_design", "open_design", "load_example", "clear_grid", "import_gds",
    };

    private static readonly JsonElement NoArguments = JsonDocument.Parse("{}").RootElement.Clone();

    private readonly AgentDesignSession _session;
    private readonly Func<AgentToolCatalog> _catalog;

    /// <summary>Creates the tool; the catalog is resolved lazily because it contains this tool.</summary>
    public BatchTool(AgentDesignSession session, Func<AgentToolCatalog> catalog)
    {
        _session = session;
        _catalog = catalog;
    }

    /// <inheritdoc/>
    public string Name => "batch";

    /// <inheritdoc/>
    public string Description =>
        "Run several tool calls in order in one request: steps = [{tool, arguments}]. atomic=true (default) undoes " +
        "every edit of the batch when a step fails, so the design is changed fully or not at all. Returns each step's " +
        "result. Session-replacing tools (new_design, open_design, load_example, clear_grid, import_gds) are not allowed.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            steps = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new { tool = new { type = "string" }, arguments = new { type = "object" } },
                    required = new[] { "tool" },
                },
            },
            atomic = new { type = "boolean", description = "Roll back the batch's edits when a step fails. Default true." },
        },
        required = new[] { "steps" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) =>
        Task.FromResult(AgentToolResult.Error("batch runs only through the tool catalog."));

    /// <inheritdoc/>
    public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context)
    {
        if (!arguments.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
            return AgentToolResult.Error("Pass steps: [{\"tool\": \"...\", \"arguments\": {...}}, ...].");
        foreach (var step in steps.EnumerateArray())
        {
            var name = ToolArguments.GetString(step, "tool");
            if (name == null || NotBatchable.Contains(name))
                return AgentToolResult.Error($"'{name}' cannot run inside a batch.");
        }

        var catalog = _catalog();
        var atomic = ToolArguments.GetBool(arguments, "atomic", fallback: true);
        var undoBefore = _session.Commands.UndoCount;
        var results = new JsonArray();
        var failed = false;
        foreach (var step in steps.EnumerateArray())
        {
            var name = ToolArguments.GetString(step, "tool")!;
            var stepArguments = step.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object ? a : NoArguments;
            var result = await catalog.InvokeWithinAsync(name, stepArguments, context);
            results.Add(Describe(name, result));
            if (!result.IsError) continue;
            failed = true;
            break;
        }

        var rolledBack = failed && atomic && await RollBackAsync(catalog, context, undoBefore);
        var summary = new JsonObject
        {
            ["completed"] = !failed,
            ["steps_run"] = results.Count,
            ["rolled_back"] = rolledBack,
            ["results"] = results,
        };
        return new AgentToolResult(new[] { new AgentToolContent("text", summary.ToJsonString()) }, IsError: failed);
    }

    private async Task<bool> RollBackAsync(AgentToolCatalog catalog, AgentToolContext context, int undoBefore)
    {
        while (_session.Commands.UndoCount > undoBefore)
        {
            if ((await catalog.InvokeWithinAsync("undo", NoArguments, context)).IsError)
                return false;
        }
        return true;
    }

    private static JsonObject Describe(string tool, AgentToolResult result)
    {
        var text = result.Content.FirstOrDefault(c => c.Type == "text")?.Text;
        JsonNode? body = text;
        if (text != null && (text.StartsWith('{') || text.StartsWith('[')))
        {
            try { body = JsonNode.Parse(text); }
            catch (JsonException) { }
        }
        return new JsonObject { ["tool"] = tool, ["is_error"] = result.IsError, ["result"] = body };
    }
}
