using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

/// <summary>Axis-aligned bounds in µm.</summary>
public sealed record BoundsInfo(double MinX, double MinY, double MaxX, double MaxY);

/// <summary>Agent-facing description of a top-level canvas component.</summary>
public sealed record ComponentInfo(
    string Id, string Name, string? Type, double X, double Y, double Rotation,
    string? Group, bool IsGroup, int ChildCount, bool Locked, BoundsInfo Bounds);

/// <summary>Agent-facing description of a waveguide connection.</summary>
public sealed record ConnectionInfo(
    string Id, string Start, string End, string StartComponent, string EndComponent,
    double LengthUm, bool Blocked, string? FailureReason, bool IsCrossingPiece, bool Locked);

/// <summary>
/// Maps canvas view-models to the plain records the agent-bridge tools serialize.
/// Endpoints are written as <c>component.pin</c> with the leaf component that owns the pin;
/// <c>StartComponent</c>/<c>EndComponent</c> name the top-level component (or group) to
/// move or delete when acting on that endpoint.
/// </summary>
public static class DesignDescriber
{
    private const int Decimals = 3;

    /// <summary>Describes one top-level component.</summary>
    public static ComponentInfo Describe(ComponentViewModel vm)
    {
        var component = vm.Component;
        var bounds = BoundingBoxCalculator.Calculate(new[] { vm }) is { } box
            ? new BoundsInfo(Round(box.MinX), Round(box.MinY), Round(box.MaxX), Round(box.MaxY))
            : new BoundsInfo(Round(vm.X), Round(vm.Y), Round(vm.X + vm.Width), Round(vm.Y + vm.Height));
        var group = component as ComponentGroup;

        return new ComponentInfo(
            component.Identifier,
            vm.Name,
            vm.TemplateName ?? vm.ComponentTypeName,
            Round(vm.X),
            Round(vm.Y),
            Round(component.RotationDegrees),
            (component.ParentGroup as ComponentGroup)?.Identifier,
            group != null,
            group?.ChildComponents.Count ?? 0,
            component.IsLocked,
            bounds);
    }

    /// <summary>
    /// Describes one connection. A piece of a recorded crossing (<paramref name="isCrossingPiece"/>,
    /// from the crossing registry) and a piece docked on any crossing component — chain
    /// crossings carry no record — both count as crossing pieces.
    /// </summary>
    public static ConnectionInfo Describe(WaveguideConnection connection, bool isCrossingPiece)
    {
        isCrossingPiece |= CrossingComponentCatalog.IsCrossing(connection.StartPin.ParentComponent)
            || CrossingComponentCatalog.IsCrossing(connection.EndPin.ParentComponent);
        var blocked = connection.IsBlockedFallback;
        return new ConnectionInfo(
            connection.Id.ToString(),
            Endpoint(connection.StartPin),
            Endpoint(connection.EndPin),
            TopLevelOf(connection.StartPin.ParentComponent).Identifier,
            TopLevelOf(connection.EndPin.ParentComponent).Identifier,
            Round(connection.PathLengthMicrometers),
            blocked,
            blocked ? DescribeFailure(connection.FailureReason) : null,
            isCrossingPiece,
            connection.IsLocked);
    }

    /// <summary>Snake-case failure reason with a short explanation the agent can act on.</summary>
    public static string DescribeFailure(RoutingFailureReason reason) => reason switch
    {
        RoutingFailureReason.EndpointBlocked =>
            "endpoint_blocked: a component body seals a pin's escape corridor; move a component",
        RoutingFailureReason.Contention =>
            "contention: other routed waveguides block the path; reroute or make room",
        _ => "unknown: the router found no valid path",
    };

    /// <summary>The outermost group containing <paramref name="component"/> (or the component itself).</summary>
    public static Component TopLevelOf(Component component)
    {
        var current = component;
        while (current.ParentGroup is ComponentGroup parent)
            current = parent;
        return current;
    }

    private static string Endpoint(PhysicalPin pin) => $"{pin.ParentComponent.Identifier}.{pin.Name}";

    private static double Round(double value) => Math.Round(value, Decimals);
}
