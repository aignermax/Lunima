using System.Globalization;
using System.Text.Json;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Author;

/// <summary>
/// <c>match_lengths</c>: equalises the optical path length of several wires with meanders —
/// the routing panel's Length Equalisation (bundle reroute, radius search, process bend floors,
/// one undo step) driven on the given wires instead of the canvas selection.
/// </summary>
public sealed class MatchLengthsTool : IAgentBridgeTool
{
    private const int MinimumWires = 2;

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public MatchLengthsTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "match_lengths";

    /// <inheritdoc/>
    public string Description =>
        "Equalise the lengths of two or more wires (connection ids from list_connections / connect_bundle) by adding " +
        "meanders, e.g. MZI arms or a phased-array bundle; optional target_length_um (default: the longest), " +
        "max/min meander bend radius. Undoable. Returns the lengths before and after and the remaining skew.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            connection_ids = new { type = "array", items = new { type = "string" } },
            target_length_um = new { type = "number", description = "Common target length; default the longest wire." },
            max_radius_um = new { type = "number", description = "Largest meander bend radius to try." },
            min_radius_um = new { type = "number", description = "Smallest meander bend radius allowed." },
        },
        required = new[] { "connection_ids" },
    };

    /// <inheritdoc/>
    public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var panel = _session.Main?.BottomPanel.LengthEqualization;
        if (panel == null)
            return AgentToolResult.Error("The main window is not ready yet.");
        var ids = ToolArguments.GetStringList(arguments, "connection_ids");
        var wires = ids.Select(_session.FindConnection).ToList();
        if (wires.Count < MinimumWires || wires.Any(w => w == null))
            return AgentToolResult.Error("Pass at least two existing connection_ids (see list_connections).");
        var before = wires.Select(w => Math.Round(w!.Connection.PathLengthMicrometers, 3)).ToList();

        var selection = _session.Canvas.Selection;
        selection.SelectedConnections.Clear();
        foreach (var wire in wires)
            selection.SelectedConnections.Add(wire!);
        panel.TargetLengthText = Format(ToolArguments.GetDouble(arguments, "target_length_um"));
        if (ToolArguments.GetDouble(arguments, "max_radius_um") is double max) panel.MaxRadiusText = Format(max);
        if (ToolArguments.GetDouble(arguments, "min_radius_um") is double min) panel.MinRadiusText = Format(min);

        await panel.EqualizeCommand.ExecuteAsync(null);
        await Edit.RoutingAwaiter.WaitForIdleAsync(_session.Canvas);
        var after = wires.Select(w => Math.Round(w!.Connection.PathLengthMicrometers, 3)).ToList();
        var status = panel.StatusMessage;
        var failed = panel.HasErrorStatus;
        selection.SelectedConnections.Clear();

        var report = new
        {
            Status = status,
            LengthsBeforeUm = before,
            LengthsAfterUm = after,
            SkewUm = Math.Round(after.Max() - after.Min(), 3),
            Connections = wires.Select(w => DesignDescriber.Describe(w!.Connection, isCrossingPiece: false)).ToList(),
        };
        return failed
            ? new AgentToolResult(AgentToolResult.Json(report).Content, IsError: true)
            : AgentToolResult.Json(report);
    }

    private static string Format(double? value) =>
        value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "";
}
