using System.Text.Json;

namespace CAP.Avalonia.Services.AgentBridge.Tools;

/// <summary>
/// A tool exposed to external agents over the MCP agent bridge. Implementations run on the
/// Avalonia UI thread (the <see cref="AgentToolCatalog"/> marshals every call), so they may
/// touch view-models directly but must not block: long work (routing) is awaited, not waited.
/// Existing in-app assistant tools (<see cref="AiTools.IAiTool"/>) are exposed through
/// <see cref="AiToolAdapter"/> instead of being re-implemented.
/// </summary>
public interface IAgentBridgeTool
{
    /// <summary>MCP tool name (snake_case, unique across the catalog).</summary>
    string Name { get; }

    /// <summary>Description shown to the agent to guide tool selection.</summary>
    string Description { get; }

    /// <summary>JSON-schema object describing the arguments.</summary>
    object InputSchema { get; }

    /// <summary>Runs the tool with the call's <c>arguments</c> object.</summary>
    Task<AgentToolResult> ExecuteAsync(JsonElement arguments);

    /// <summary>
    /// Runs an admitted call. Async implementations must propagate cancellation and guard
    /// each later mutation with the context; the legacy overload guards the synchronous prefix only.
    /// </summary>
    Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context)
    {
        Task<AgentToolResult> execution = null!;
        context.Commit(() => execution = ExecuteAsync(arguments));
        return execution;
    }
}
