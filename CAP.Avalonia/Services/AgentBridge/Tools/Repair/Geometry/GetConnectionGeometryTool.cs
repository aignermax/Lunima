using System.Text.Json;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;
using CAP_Core.Components.Connections;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Geometry;

/// <summary>
/// <c>get_connection_geometry</c>: a connection's path segments (straights numbered for
/// <c>shift_segment</c>, arcs with centre/radius/sweep), its two pins (position and facing),
/// and the components and wires within a radius — what an agent needs to reason about why a
/// wire is blocked and what to change.
/// </summary>
public sealed class GetConnectionGeometryTool : IAgentBridgeTool
{
    private const double DefaultRadiusMicrometers = 30;

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public GetConnectionGeometryTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "get_connection_geometry";

    /// <inheritdoc/>
    public string Description =>
        "Geometry of one connection: its segments (type straight|arc, start/end µm, angles; straights carry " +
        "straight_index and shiftable for shift_segment; arcs carry center, radius, sweep), both pins (position, " +
        "facing angle: 0=+x, 90=+y down), the blocked flag, and the top-level components and wires within " +
        "radius_um (default 30) of the path. A blocked wire's path is its red placeholder, not a route.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            id = new { type = "string", description = "Connection id from list_connections." },
            radius_um = new { type = "number", description = "Search radius for nearby obstacles in µm (default 30)." },
        },
        required = new[] { "id" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var id = ToolArguments.GetString(arguments, "id") ?? "";
        var connection = _session.FindConnection(id)?.Connection;
        if (connection == null)
            return Task.FromResult(AgentToolResult.Error($"Unknown connection id '{id}'. Use list_connections."));
        var radius = Math.Max(0, ToolArguments.GetDouble(arguments, "radius_um") ?? DefaultRadiusMicrometers);

        var info = DesignDescriber.Describe(connection, false);
        return Task.FromResult(AgentToolResult.Json(new
        {
            info.Id,
            info.Blocked,
            info.FailureReason,
            info.LengthUm,
            StartPin = ConnectionGeometryDescriber.Pin(connection.StartPin),
            EndPin = ConnectionGeometryDescriber.Pin(connection.EndPin),
            ShiftOffsets = connection.StraightShiftOffsets.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            Segments = ConnectionGeometryDescriber.Segments(connection),
            NearbyComponents = NearbyComponents(connection, radius),
            NearbyWires = NearbyWires(connection, radius),
        }));
    }

    private IReadOnlyList<ComponentInfo> NearbyComponents(WaveguideConnection connection, double radius)
    {
        var samples = ConnectionGeometryDescriber.Samples(connection).ToList();
        return _session.Canvas.Components
            .Select(DesignDescriber.Describe)
            .Where(c => samples.Any(p => p.X >= c.Bounds.MinX - radius && p.X <= c.Bounds.MaxX + radius
                                      && p.Y >= c.Bounds.MinY - radius && p.Y <= c.Bounds.MaxY + radius))
            .ToList();
    }

    private IReadOnlyList<object> NearbyWires(WaveguideConnection connection, double radius) =>
        _session.Canvas.Connections
            .Select(vm => vm.Connection)
            .Where(c => c != connection)
            .Select(c => (Wire: c, Distance: ConnectionGeometryDescriber.Distance(connection, c)))
            .Where(x => x.Distance is { } d && d <= radius)
            .OrderBy(x => x.Distance)
            .Select(x => (object)new
            {
                Id = x.Wire.Id.ToString(),
                Start = $"{x.Wire.StartPin.ParentComponent.Identifier}.{x.Wire.StartPin.Name}",
                End = $"{x.Wire.EndPin.ParentComponent.Identifier}.{x.Wire.EndPin.Name}",
                DistanceUm = ConnectionGeometryDescriber.Round(x.Distance!.Value),
                Blocked = x.Wire.IsBlockedFallback,
            })
            .ToList();
}
