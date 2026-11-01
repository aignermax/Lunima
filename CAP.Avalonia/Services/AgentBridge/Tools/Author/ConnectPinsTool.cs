using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;
using CAP_Core.Components.Core;
using CAP_Core.Components.PinKinds;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Author;

/// <summary>
/// <c>create_connection</c>: wires two components with a routed waveguide through
/// <see cref="CreateConnectionCommand"/> (undoable), waits for the router and reports the
/// resulting route. Pins may be named; otherwise the closest pair of free, compatible pins is
/// used. Supersedes the in-app assistant's variant, which cannot choose pins.
/// </summary>
public sealed class ConnectPinsTool : IAgentBridgeTool
{
    private const double FacingBonusMicrometers = 50;

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public ConnectPinsTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "create_connection";

    /// <inheritdoc/>
    public string Description =>
        "Connect two components with a routed waveguide (or metal trace for electrical pins). Name the pins " +
        "(from_pin/to_pin, see get_component) or omit them to use the closest free compatible pins. Replaces an " +
        "existing wire on either pin. Undoable. Waits for routing; returns the wire (length, loss, blocked + reason).";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            from_component = new { type = "string", description = "Source component id or unique display name." },
            from_pin = new { type = "string", description = "Source pin name (optional)." },
            to_component = new { type = "string", description = "Target component id or unique display name." },
            to_pin = new { type = "string", description = "Target pin name (optional)." },
        },
        required = new[] { "from_component", "to_component" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(arguments, null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) => ExecuteCoreAsync(arguments, context);

    private async Task<AgentToolResult> ExecuteCoreAsync(JsonElement arguments, AgentToolContext? context)
    {
        var fromId = ToolArguments.GetString(arguments, "from_component") ?? "";
        var toId = ToolArguments.GetString(arguments, "to_component") ?? "";
        var from = _session.FindComponent(fromId);
        var to = _session.FindComponent(toId);
        if (from == null || to == null)
            return AgentToolResult.Error($"No top-level component with id '{(from == null ? fromId : toId)}'. Use list_components.");
        if (from == to)
            return AgentToolResult.Error("Both ends are the same component; connect two different components.");

        var pair = ResolvePins(from.Component, ToolArguments.GetString(arguments, "from_pin"),
            to.Component, ToolArguments.GetString(arguments, "to_pin"), out var error);
        if (pair == null)
            return AgentToolResult.Error(error!);
        var (start, end) = pair.Value;
        var command = new CreateConnectionCommand(_session.Canvas, start.Pin, end.Pin);
        if (!command.ArePinKindsCompatible)
            return AgentToolResult.Error($"{start.Name} and {end.Name} are in different domains (optical vs electrical).");
        if (command.CrossesDieEdge)
            return AgentToolResult.Error("A waveguide cannot cross a die edge; link dies through edge couplers (link_dies).");

        await using var edit = new AgentDocumentOperation(_session.Canvas, context);
        edit.Commit(() => _session.Commands.ExecuteCommand(command));
        await command.HomingCompletion;
        var routed = await edit.RouteAsync();
        edit.Commit(_session.Canvas.InvalidateSimulation);

        var wire = _session.Canvas.Connections
            .Select(c => c.Connection)
            .FirstOrDefault(c => Joins(c, start.Pin, end.Pin));
        return AgentToolResult.Json(new
        {
            From = $"{from.Component.Identifier}.{start.Name}",
            To = $"{to.Component.Identifier}.{end.Name}",
            Connection = wire == null ? null : DesignDescriber.Describe(wire, isCrossingPiece: false),
            RoutingComplete = routed,
        });
    }

    private static bool Joins(CAP_Core.Components.Connections.WaveguideConnection wire, PhysicalPin a, PhysicalPin b) =>
        (wire.StartPin == a && wire.EndPin == b) || (wire.StartPin == b && wire.EndPin == a);

    private (AgentPin Start, AgentPin End)? ResolvePins(
        Component from, string? fromPin, Component to, string? toPin, out string? error)
    {
        error = null;
        var starts = Candidates(from, fromPin, ref error);
        var ends = Candidates(to, toPin, ref error);
        if (error != null)
            return null;

        var best = starts
            .SelectMany(s => ends.Select(e => (Start: s, End: e)))
            .Where(p => PinKindHelper.ArePlanarRoutingCompatible(p.Start.Pin, p.End.Pin))
            .OrderBy(p => Score(p.Start.Pin, p.End.Pin))
            .FirstOrDefault();
        if (best.Start == null)
            error = $"No free compatible pin pair between '{from.Identifier}' and '{to.Identifier}'. Name the pins (see get_component).";
        return best.Start == null ? null : best;
    }

    private IReadOnlyList<AgentPin> Candidates(Component component, string? name, ref string? error)
    {
        if (name != null)
        {
            var pin = ComponentPins.Find(component, name);
            if (pin != null)
                return new[] { pin };
            error ??= $"'{component.Identifier}' has no pin '{name}'. Pins: {string.Join(", ", ComponentPins.Of(component).Select(p => p.Name))}.";
            return Array.Empty<AgentPin>();
        }

        var used = _session.Canvas.Connections
            .SelectMany(c => new[] { c.Connection.StartPin, c.Connection.EndPin })
            .ToHashSet();
        return ComponentPins.Of(component).Where(p => !used.Contains(p.Pin)).ToList();
    }

    /// <summary>Distance between the pins, rewarding pins that face each other.</summary>
    private static double Score(PhysicalPin start, PhysicalPin end)
    {
        var (sx, sy) = start.GetAbsolutePosition();
        var (ex, ey) = end.GetAbsolutePosition();
        var distance = Math.Sqrt((ex - sx) * (ex - sx) + (ey - sy) * (ey - sy));
        var opposed = Math.Abs(Math.Abs(NormalizeAngle(start.GetAbsoluteAngle() - end.GetAbsoluteAngle())) - 180) < 1;
        return opposed ? distance - FacingBonusMicrometers : distance;
    }

    private static double NormalizeAngle(double degrees)
    {
        var a = degrees % 360;
        return a > 180 ? a - 360 : a < -180 ? a + 360 : a;
    }
}
