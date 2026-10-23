using System.Collections.Immutable;
using CAP_Core.Components.Connections;
using CAP_Core.LightCalculation.BendLoss;

namespace CAP_Core.Routing.Snapshots;

/// <summary>Immutable calculated geometry and scalar results, with no live physics model.</summary>
internal sealed record ComputedRouteValue(Guid Id, RouteGeometryValue? Geometry, ConnectionLossBreakdown Loss,
    bool Frozen, ImmutableDictionary<int, double> BendOverrides, ImmutableDictionary<int, double> Shifts,
    ImportedRouteValue Imported)
{
    internal static ComputedRouteValue Capture(WaveguideConnection wire) => new(wire.Id,
        RouteGeometryValue.Capture(wire.RoutedPath), wire.LossBreakdown with { Bends = wire.LossBreakdown.Bends.ToImmutableArray() },
        wire.IsRouteFrozen, wire.BendRadiusOverrides.ToImmutableDictionary(), wire.StraightShiftOffsets.ToImmutableDictionary(),
        ImportedRouteValue.Capture(wire));

    internal PreparedRouteValue Prepare(WaveguideConnection target, bool preservePath = false) => new(target,
        preservePath ? target.RoutedPath : Geometry?.Build(), Loss, Frozen,
        BendOverrides, Shifts, PolygonValue.BuildDrawn(Imported.Polygons), Imported.Signature,
        Imported.OriginalPath?.Build(), Imported.Displaced);
}

internal sealed record PreparedRouteValue(WaveguideConnection Target, RoutedPath? Path, ConnectionLossBreakdown Loss,
    bool Frozen, ImmutableDictionary<int, double> BendOverrides, ImmutableDictionary<int, double> Shifts,
    AsDrawnGeometry? Imported, RouteShapeSignature? Signature, RoutedPath? Original, bool Displaced)
{
    internal void Apply()
    {
        Target.IsRouteFrozen = Frozen;
        Target.BendRadiusOverrides.Clear();
        foreach (var entry in BendOverrides) Target.BendRadiusOverrides.Add(entry.Key, entry.Value);
        Target.StraightShiftOffsets.Clear();
        foreach (var entry in Shifts) Target.StraightShiftOffsets.Add(entry.Key, entry.Value);
        Target.RestoreImportedRouteState(Imported, Signature, Original, Displaced);
        Target.ApplyComputedRoute(Path, Loss);
    }
}
