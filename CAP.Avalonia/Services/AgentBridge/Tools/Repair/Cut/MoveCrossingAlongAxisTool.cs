using System.Globalization;
using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;
using CAP_Core.Routing.CrossingInsertion;
using CAP_Core.Routing.CrossingInsertion.CrossingMove;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Cut;

/// <summary>
/// <c>move_crossing_along_axis</c>: slides a crossing (router-placed or user-placed) along one
/// of its two wires through <see cref="MoveCrossingAlongAxisCommand"/> — the wire along the
/// axis keeps its route, the crossing wire routes again — as one undo step. Refusals carry
/// the command's own validation message.
/// </summary>
public sealed class MoveCrossingAlongAxisTool : IAgentBridgeTool
{
    private const int Decimals = 3;

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public MoveCrossingAlongAxisTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "move_crossing_along_axis";

    /// <inheritdoc/>
    public string Description =>
        "Slide a waveguide crossing (id from list_components; crossings are named 'Waveguide Crossing…') along " +
        "one of its wires: axis=horizontal moves along x (+ = right), axis=vertical along y (+ = down), by " +
        "delta_um. The wire along the axis keeps its route, the other one re-routes. A router-placed crossing " +
        "becomes user-owned. Refused (with the reason) when the moved crossing would overlap a component or sit " +
        "on another wire. One undo step; waits for routing.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            crossing_id = new { type = "string", description = "Crossing component id." },
            axis = new { type = "string", @enum = new[] { "horizontal", "vertical" }, description = "Axis to slide along." },
            delta_um = new { type = "number", description = "Signed distance in µm (+x right, +y down)." },
        },
        required = new[] { "crossing_id", "axis", "delta_um" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(arguments, null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) => ExecuteCoreAsync(arguments, context);

    private async Task<AgentToolResult> ExecuteCoreAsync(JsonElement arguments, AgentToolContext? context)
    {
        await using var edit = new AgentDocumentOperation(_session.Canvas, context);
        var id = ToolArguments.GetString(arguments, "crossing_id") ?? "";
        var vm = _session.FindComponent(id);
        if (vm == null || !CrossingComponentCatalog.IsCrossing(vm.Component))
            return AgentToolResult.Error($"No crossing with id '{id}'. Crossings are listed by list_components.");
        if (!TryParseAxis(ToolArguments.GetString(arguments, "axis"), out var axis))
            return AgentToolResult.Error("axis must be 'horizontal' or 'vertical'.");
        var delta = ToolArguments.GetDouble(arguments, "delta_um");
        if (delta == null)
            return AgentToolResult.Error("Pass delta_um (µm).");

        var refusal = MoveCrossingAlongAxisCommand.Validate(_session.Canvas, vm.Component, axis, delta.Value);
        if (refusal != null)
            return AgentToolResult.Error("Cannot move the crossing: " + refusal);

        var (fromX, fromY) = (vm.X, vm.Y);
        var command = new MoveCrossingAlongAxisCommand(_session.Canvas, vm.Component, axis, delta.Value);
        var routed = await RepairEdit.ExecuteAndRouteAsync(_session, command, edit);
        if (!command.Succeeded)
        {
            await RepairEdit.UndoAndRouteAsync(_session, edit);
            return AgentToolResult.Error("Cannot move the crossing: " + (command.Error ?? "unknown reason"));
        }

        return AgentToolResult.Json(new
        {
            Moved = vm.Component.Identifier,
            From = new { X = Math.Round(fromX, Decimals), Y = Math.Round(fromY, Decimals) },
            To = new { X = Math.Round(vm.X, Decimals), Y = Math.Round(vm.Y, Decimals) },
            PatchedPieces = command.LastPlan?.Patched.Count ?? 0,
            ReroutedPieces = command.LastPlan?.Rerouted.Count ?? 0,
            RoutingComplete = routed,
            Routing = ConnectionSnapshot.Report(_session),
        });
    }

    /// <summary>Parses <c>horizontal</c>/<c>vertical</c> (also <c>x</c>/<c>y</c>), case-insensitive.</summary>
    public static bool TryParseAxis(string? text, out CrossingAxis axis)
    {
        switch (text?.Trim().ToLower(CultureInfo.InvariantCulture))
        {
            case "horizontal" or "x":
                axis = CrossingAxis.Horizontal;
                return true;
            case "vertical" or "y":
                axis = CrossingAxis.Vertical;
                return true;
            default:
                axis = default;
                return false;
        }
    }
}
