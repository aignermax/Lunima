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
    private readonly AgentOperationGate _operations = new();
    private readonly SemaphoreSlim _execution = new(1, 1);

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
    public async Task<AgentToolResult> InvokeAsync(
        string name, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var tool = _tools[name];
        using var context = _operations.Enter(cancellationToken);
        await _execution.WaitAsync(context.CancellationToken);
        try
        {
            var result = await _dispatcher.InvokeAsync(async () =>
            {
                context.ThrowIfStopped();
                try { return await tool.ExecuteAsync(arguments, context); }
                catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
                {
                    return AgentToolResult.Error($"{name} failed: {ex.Message}");
                }
            });
            // Tool completion includes asynchronous disposal. A result prepared before
            // that cleanup may no longer belong to an admitted request afterwards.
            context.ThrowIfStopped();
            return result;
        }
        finally { _execution.Release(); }
    }

    /// <summary>Opens fresh admission after the previous server generation drained.</summary>
    public void OpenAdmission() => _operations.Open();

    /// <summary>Revokes mutation permission before asynchronously draining accepted tools.</summary>
    public Task CloseAdmissionAsync() => _operations.CloseAsync();

    /// <summary>Cancellation callback diagnostics, available once admission has drained.</summary>
    public AggregateException? CancellationError => _operations.CancellationError;
}
