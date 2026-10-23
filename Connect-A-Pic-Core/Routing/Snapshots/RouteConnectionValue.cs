using System.Collections.Immutable;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation.BendLoss;

namespace CAP_Core.Routing.Snapshots;

internal sealed record RouteSettingsValue(WaveguideType Type, double Width, double Radius, bool Frozen,
    double PropagationLoss, double ExcessBendLoss, BendLossModel? BendLoss, RoutingDispersionValue? Dispersion,
    ImmutableDictionary<int, double> RadiusOverrides, ImmutableDictionary<int, double> Shifts, int? Layer, int? DataType)
{
    internal static RouteSettingsValue Capture(WaveguideConnection wire) => new(wire.Type, wire.WidthMicrometers,
        wire.BendRadiusMicrometers, wire.IsRouteFrozen, wire.PropagationLossDbPerCm, wire.ExcessBendLossDbPer90Deg,
        wire.BendLoss, RoutingDispersionValue.Capture(wire.DispersionModel), wire.BendRadiusOverrides.ToImmutableDictionary(),
        wire.StraightShiftOffsets.ToImmutableDictionary(), wire.SourceGdsLayer, wire.SourceGdsDataType);

    internal static RouteSettingsValue Capture(FrozenWaveguidePath path) => new(path.ConnectionType, path.WidthMicrometers,
        path.BendRadiusMicrometers, path.IsRouteFrozen, path.PropagationLossDbPerCm, path.ExcessBendLossDbPer90Deg,
        path.BendLoss, RoutingDispersionValue.Capture(path.DispersionModel), path.BendRadiusOverrides.ToImmutableDictionary(),
        path.StraightShiftOffsets.ToImmutableDictionary(), path.Layer, path.DataType);

    internal void Apply(WaveguideConnection wire)
    {
        wire.Type = Type;
        wire.WidthMicrometers = Width;
        wire.BendRadiusMicrometers = Radius;
        wire.IsRouteFrozen = Frozen;
        wire.PropagationLossDbPerCm = PropagationLoss;
        wire.ExcessBendLossDbPer90Deg = ExcessBendLoss;
        wire.BendLoss = BendLoss ?? BendLossPresets.Generic;
        wire.DispersionModel = Dispersion?.Build();
        wire.SourceGdsLayer = Layer;
        wire.SourceGdsDataType = DataType;
        foreach (var pair in RadiusOverrides) wire.BendRadiusOverrides.Add(pair.Key, pair.Value);
        foreach (var pair in Shifts) wire.StraightShiftOffsets.Add(pair.Key, pair.Value);
    }

    internal void Apply(FrozenWaveguidePath path)
    {
        path.ConnectionType = Type;
        path.WidthMicrometers = Width;
        path.BendRadiusMicrometers = Radius;
        path.IsRouteFrozen = Frozen;
        path.PropagationLossDbPerCm = PropagationLoss;
        path.ExcessBendLossDbPer90Deg = ExcessBendLoss;
        path.BendLoss = BendLoss;
        path.DispersionModel = Dispersion?.Build();
        path.Layer = Layer;
        path.DataType = DataType;
        foreach (var pair in RadiusOverrides) path.BendRadiusOverrides.Add(pair.Key, pair.Value);
        foreach (var pair in Shifts) path.StraightShiftOffsets.Add(pair.Key, pair.Value);
    }
}

internal sealed record RouteConnectionValue(Guid Id, Guid Start, Guid End, RouteSettingsValue Settings,
    RouteGeometryValue? Geometry, ImportedRouteValue Imported, bool Locked, double? TargetLength,
    double? LengthTolerance, double FacetGapIndex, double ProcessFloor)
{
    internal static RouteConnectionValue Capture(WaveguideConnection wire, WaveguideRouter router)
    {
        if (wire.StartPin == null || wire.EndPin == null)
            throw new InvalidDataException("A routed connection requires both endpoint pins.");
        return new(wire.Id, wire.StartPin.PinId, wire.EndPin.PinId, RouteSettingsValue.Capture(wire),
            RouteGeometryValue.Capture(wire.RoutedPath), ImportedRouteValue.Capture(wire), wire.IsLocked,
            wire.TargetLengthMicrometers, wire.LengthToleranceMicrometers, wire.FacetGapMediumIndex,
            router.ResolveProcessFloorFor(wire.StartPin, wire.EndPin));
    }

    internal WaveguideConnection Build(IReadOnlyDictionary<Guid, PhysicalPin> pins)
    {
        var wire = new WaveguideConnection { Id = Id, StartPin = pins[Start], EndPin = pins[End], IsLocked = Locked,
            TargetLengthMicrometers = TargetLength, LengthToleranceMicrometers = LengthTolerance, FacetGapMediumIndex = FacetGapIndex };
        Settings.Apply(wire);
        if (Geometry != null) wire.RestoreCachedPath(Geometry.Build());
        Imported.Restore(wire);
        return wire;
    }
}

internal sealed record FrozenRouteValue(Guid Id, Guid? Start, Guid? End, RouteSettingsValue Settings,
    RouteGeometryValue? Geometry, ImmutableArray<PolygonValue> Polygons, RouteShapeSignature? DrawnSignature)
{
    internal static FrozenRouteValue Capture(FrozenWaveguidePath path)
    {
        var imported = path.CaptureImportedGeometryState();
        return new(path.PathId, path.StartPin?.PinId, path.EndPin?.PinId, RouteSettingsValue.Capture(path),
            RouteGeometryValue.Capture(path.Path), PolygonValue.Capture(imported.Geometry?.Polygons), imported.Signature);
    }
    internal FrozenWaveguidePath Build(IReadOnlyDictionary<Guid, PhysicalPin> pins)
    {
        var path = new FrozenWaveguidePath { PathId = Id, Path = Geometry?.Build()!,
            StartPin = Start is { } start ? pins[start] : null, EndPin = End is { } end ? pins[end] : null };
        Settings.Apply(path);
        path.RestoreImportedGeometryState(PolygonValue.BuildDrawn(Polygons), DrawnSignature);
        return path;
    }
}
