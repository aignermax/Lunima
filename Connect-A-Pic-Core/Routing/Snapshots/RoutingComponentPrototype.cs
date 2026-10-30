using System.Collections.Immutable;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;

namespace CAP_Core.Routing.Snapshots;

/// <summary>Detached component definition and process rules for manual edits on a private routing graph.</summary>
public sealed class RoutingComponentPrototype
{
    internal CrossingPrototypeValue Value { get; }
    internal ImmutableDictionary<(Guid, Guid), double> Floors { get; }

    private RoutingComponentPrototype(CrossingPrototypeValue value, ImmutableDictionary<(Guid, Guid), double> floors)
    { Value = value; Floors = floors; }

    public static RoutingComponentPrototype Capture(Component prototype, IEnumerable<Component> components, WaveguideRouter router) =>
        new(CrossingPrototypeValue.Capture(prototype), RouteProcessFloorTable.CapturePrototypePairs(prototype, components, router));
}
