using System.Text.Json;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Dies;

/// <summary>
/// <c>list_dies</c>: the dies of the assembly (name, process, outline, enabled PDKs, parts,
/// edge-coupler facets on the edge) and every die-to-die link with gap, offset and loss.
/// </summary>
public sealed class ListDiesTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public ListDiesTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "list_dies";

    /// <inheritdoc/>
    public string Description =>
        "List the dies (chiplets) of a multi-die assembly: name, process, outline (µm), enabled PDKs, " +
        "number of parts and edge-coupler facets on the die edge, plus every die-to-die link with facet gap, " +
        "lateral offset, gap medium index and coupling loss (dB at 1550 nm). A single-chip design has no dies " +
        "(IsAssembly false) — create_die turns it into an assembly.";

    /// <inheritdoc/>
    public object InputSchema => new { type = "object", properties = new { } };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) =>
        Task.FromResult(AgentToolResult.Json(new
        {
            IsAssembly = _session.Canvas.Dies.IsAssemblyRoot,
            Dies = _session.Canvas.Dies.RootDies.Select(DieToolSupport.Describe).ToList(),
            Links = DieToolSupport.Links(_session).ToList(),
            Processes = _session.Processes.Select(p => p.DisplayName).ToList(),
        }));
}
