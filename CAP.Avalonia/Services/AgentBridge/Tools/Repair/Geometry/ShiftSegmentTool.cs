using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.AgentBridge.Tools.Edit;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Routing.InterconnectRouting.SegmentShift;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Geometry;

/// <summary>
/// <c>shift_segment</c>: moves a straight segment of a routed connection parallel to itself
/// through the canvas' segment-shift editor (<see cref="SegmentShiftCommand"/>, the in-canvas
/// midpoint-handle drag) — one undo step. The shift freezes that wire's route; the other wires
/// re-route around it.
/// </summary>
public sealed class ShiftSegmentTool : IAgentBridgeTool
{
    private const double OffsetTolerance = 1e-6;

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public ShiftSegmentTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "shift_segment";

    /// <inheritdoc/>
    public string Description =>
        "Shift a straight segment of a routed connection perpendicular to itself by offset_um (relative to " +
        "where it is now; positive = to the left of the segment's travel direction). Only straights between two " +
        "bends are shiftable — see get_connection_geometry (straight_index, shiftable). The wire's route is " +
        "frozen afterwards; other wires re-route. One undo step; returns the new geometry and blocked state.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            connection_id = new { type = "string", description = "Connection id." },
            straight_index = new { type = "number", description = "0-based straight_index from get_connection_geometry." },
            offset_um = new { type = "number", description = "Perpendicular shift in µm, relative to the current position." },
        },
        required = new[] { "connection_id", "straight_index", "offset_um" },
    };

    /// <inheritdoc/>
    public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var id = ToolArguments.GetString(arguments, "connection_id") ?? "";
        var vm = _session.FindConnection(id);
        if (vm == null)
            return AgentToolResult.Error($"Unknown connection id '{id}'. Use list_connections.");
        var index = ToolArguments.GetDouble(arguments, "straight_index");
        var offset = ToolArguments.GetDouble(arguments, "offset_um");
        if (index == null || offset == null)
            return AgentToolResult.Error("Pass straight_index and offset_um.");

        var command = Build(_session, vm, (int)index.Value, offset.Value, out var error);
        if (command == null)
            return AgentToolResult.Error(error!);

        var target = CurrentOffset(vm.Connection, (int)index.Value) + offset.Value;
        _session.Commands.ExecuteCommand(command);
        if (Math.Abs(CurrentOffset(vm.Connection, (int)index.Value) - target) > OffsetTolerance)
        {
            await _session.Canvas.Routing.ExecuteHistoryChangeAsync(_session.Commands.Undo, () => _session.Commands.CanUndo);
            return AgentToolResult.Error("The shift was rejected: an adjoining segment would collapse or invert. Try a smaller offset.");
        }
        var routed = await RoutingAwaiter.RerouteAndWaitAsync(_session.Canvas);
        _session.Canvas.InvalidateSimulation();
        return AgentToolResult.Json(new
        {
            Shifted = id,
            StraightIndex = (int)index.Value,
            OffsetUm = offset.Value,
            Blocked = vm.Connection.IsBlockedFallback,
            Segments = ConnectionGeometryDescriber.Segments(vm.Connection),
            RoutingComplete = routed,
            Routing = ConnectionSnapshot.Report(_session),
        });
    }

    /// <summary>
    /// Builds the shift of straight <paramref name="straightIndex"/> by <paramref name="deltaMicrometers"/>
    /// (relative), or null with an <paramref name="error"/> when the editor would refuse it.
    /// A shift the editor rejects at execution (a collapsing segment) leaves the offset unchanged.
    /// </summary>
    public static SegmentShiftCommand? Build(AgentDesignSession session, WaveguideConnectionViewModel vm,
        int straightIndex, double deltaMicrometers, out string? error)
    {
        error = null;
        var connection = vm.Connection;
        if (connection.IsBlockedFallback || connection.RoutedPath == null)
        {
            error = "The connection has no routed path (blocked wires cannot be shifted).";
            return null;
        }
        var handles = SegmentShiftGeometry.GetHandles(connection.GetPathSegments());
        if (handles.All(h => h.StraightIndex != straightIndex))
        {
            error = $"Straight #{straightIndex} is not shiftable. Shiftable: [{string.Join(", ", handles.Select(h => h.StraightIndex))}].";
            return null;
        }

        var before = CurrentOffset(connection, straightIndex);
        var router = session.Canvas.Router;
        return new SegmentShiftCommand(vm, straightIndex, before, before + deltaMicrometers,
            () => SegmentShiftEditor.RefreshComponentCollision(connection, router));
    }

    /// <summary>The recorded cumulative shift of a straight (0 when never shifted).</summary>
    public static double CurrentOffset(CAP_Core.Components.Connections.WaveguideConnection connection, int straightIndex) =>
        connection.StraightShiftOffsets.TryGetValue(straightIndex, out var stored) ? stored : 0.0;
}
