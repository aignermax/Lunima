using System.Text.Json;
using CAP.Avalonia.Services.AiTools;

namespace CAP.Avalonia.Services.AgentBridge.Tools;

/// <summary>
/// Exposes an in-app assistant tool (<see cref="IAiTool"/>, from the shared
/// <see cref="IAiToolRegistry"/>) as an MCP tool, so both agents share one implementation.
/// The assistant tools report failures in their returned text; exceptions become MCP
/// tool errors.
/// </summary>
public sealed class AiToolAdapter : IAgentBridgeTool
{
    private readonly IAiTool _tool;

    /// <summary>Wraps <paramref name="tool"/>.</summary>
    public AiToolAdapter(IAiTool tool) => _tool = tool;

    /// <inheritdoc/>
    public string Name => _tool.Name;

    /// <inheritdoc/>
    public string Description => _tool.Description;

    /// <inheritdoc/>
    public object InputSchema => _tool.InputSchema;

    /// <inheritdoc/>
    public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments) =>
        AgentToolResult.Text(await _tool.ExecuteAsync(arguments));

    /// <inheritdoc/>
    public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) =>
        AgentToolResult.Text(await _tool.ExecuteAsync(arguments, context, context.CancellationToken));
}
