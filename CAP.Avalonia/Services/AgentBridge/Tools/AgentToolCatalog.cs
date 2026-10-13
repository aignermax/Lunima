using System.Text.Json;
using CAP.Avalonia.Services.AgentBridge.Hosting;
using CAP.Avalonia.Services.AiTools;

namespace CAP.Avalonia.Services.AgentBridge.Tools;

/// <summary>
/// The MCP tool list: the bridge's own tools plus every in-app assistant tool from the
/// shared <see cref="IAiToolRegistry"/> (wrapped by <see cref="AiToolAdapter"/>), so a
/// tool added for the assistant automatically shows up for external agents too. A bridge
/// tool wins when both define the same name. Every call is marshalled onto the UI thread.
/// </summary>
public sealed class AgentToolCatalog
{
    private readonly Dictionary<string, IAgentBridgeTool> _tools;
    private readonly IUiDispatcher _dispatcher;

    /// <summary>Builds the catalog.</summary>
    public AgentToolCatalog(
        IEnumerable<IAgentBridgeTool> bridgeTools,
        IAiToolRegistry assistantTools,
        IUiDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _tools = new Dictionary<string, IAgentBridgeTool>(StringComparer.Ordinal);
        foreach (var tool in bridgeTools)
            _tools[tool.Name] = tool;
        foreach (var tool in assistantTools.GetAllTools())
            _tools.TryAdd(tool.Name, new AiToolAdapter(tool));
    }

    /// <summary>All tools, sorted by name for a stable <c>tools/list</c>.</summary>
    public IReadOnlyList<IAgentBridgeTool> Tools =>
        _tools.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();

    /// <summary>Whether a tool with this exact name exists.</summary>
    public bool Contains(string name) => _tools.ContainsKey(name);

    /// <summary>
    /// Runs the named tool on the UI thread. Exceptions thrown by the tool become an
    /// error result rather than a protocol error, per the MCP spec.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No tool has that name.</exception>
    public Task<AgentToolResult> InvokeAsync(string name, JsonElement arguments)
    {
        var tool = _tools[name];
        return _dispatcher.InvokeAsync(async () =>
        {
            try
            {
                return await tool.ExecuteAsync(arguments);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return AgentToolResult.Error($"{name} failed: {ex.Message}");
            }
        });
    }
}
