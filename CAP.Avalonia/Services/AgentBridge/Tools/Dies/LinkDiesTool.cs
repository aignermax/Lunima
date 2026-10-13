using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Commands.Dies;
using CAP_Core.Components.Dies;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Dies;

/// <summary>
/// <c>link_dies</c>: butt-couples a die to its neighbour the way dragging does on the canvas —
/// the nearest facing pair of edge-coupler facets is put on one axis at the link gap and every
/// aligned pair is linked. Two undo steps (move, link), like the drag.
/// </summary>
public sealed class LinkDiesTool : IAgentBridgeTool
{
    private const double SearchRadiusMicrometers = 1_000_000;

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public LinkDiesTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "link_dies";

    /// <inheritdoc/>
    public string Description =>
        "Butt-couple die 'die' to die 'to': moves 'die' so the nearest facing edge-coupler facets of the two " +
        "dies share one axis at 'gap' µm (default 2) and creates the die-to-die link for every aligned pair. " +
        "Both dies need edge couplers whose facets sit on the facing edges. Returns the links with their loss.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            die = new { type = "string", description = "Die that moves." },
            to = new { type = "string", description = "Die it couples to (stays in place)." },
            gap = new { type = "number", description = "Facet gap in µm (default 2)." },
        },
        required = new[] { "die", "to" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var moving = DieToolSupport.FindDie(_session, ToolArguments.GetString(arguments, "die"));
        var target = DieToolSupport.FindDie(_session, ToolArguments.GetString(arguments, "to"));
        if (moving == null || target == null || moving == target)
            return Task.FromResult(AgentToolResult.Error("Pass two different dies from list_dies."));
        double gap = ToolArguments.GetDouble(arguments, "gap") ?? DieLinkSnapper.DefaultGapMicrometers;

        var snap = DieLinkSnapper.Plan(moving, 0, 0, new[] { target }, gap, SearchRadiusMicrometers);
        if (snap == null)
            return Task.FromResult(AgentToolResult.Error(
                "No facing edge-coupler facets: place edge couplers on the facing edges of both dies first."));

        var canvas = _session.Canvas;
        var vm = canvas.Components.First(c => c.Component == moving);
        double x = vm.X + snap.DeltaX, y = vm.Y + snap.DeltaY;
        if (!canvas.CanMoveComponentTo(vm, x, y))
            return Task.FromResult(AgentToolResult.Error($"'{moving.GroupName}' would overlap another die at the link position."));

        _session.Commands.ExecuteCommand(new MoveComponentCommand(canvas, vm, vm.X, vm.Y, x, y));
        canvas.Dies.ApplyScopeBounds();
        if (LinkDiesCommand.TryCreate(canvas, snap.Pairs) is { } link)
            _session.Commands.ExecuteCommand(link);
        return Task.FromResult(AgentToolResult.Json(new
        {
            Moved = DieToolSupport.Describe(moving),
            Links = DieToolSupport.Links(_session).ToList(),
        }));
    }
}
