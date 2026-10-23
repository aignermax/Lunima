using System.Collections.Immutable;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation.MaterialDispersion;

namespace CAP_Core.Routing.Snapshots;

/// <summary>Scalar geometry only; reconstruction never aliases editor segments or polygon lists.</summary>
internal sealed record RouteGeometryValue(ImmutableArray<RouteSegmentValue> Segments, bool Blocked,
    bool Invalid, bool Placeholder, RoutingFailureReason Failure, bool ViolatesFloor,
    bool ThroughComponent, bool Direct, WaveguideType? Style)
{
    internal static RouteGeometryValue? Capture(RoutedPath? path) => path == null ? null : new(
        path.Segments.Select(RouteSegmentValue.Capture).ToImmutableArray(), path.IsBlockedFallback,
        path.IsInvalidGeometry, path.IsPlaceholderGeometry, path.FailureReason,
        path.ViolatesProcessMinBendRadius, path.PassesThroughComponent, path.IsDirectStyledRoute, path.DirectStyle);

    internal RoutedPath Build()
    {
        var path = new RoutedPath { IsBlockedFallback = Blocked, IsInvalidGeometry = Invalid,
            IsPlaceholderGeometry = Placeholder, FailureReason = Failure, ViolatesProcessMinBendRadius = ViolatesFloor,
            PassesThroughComponent = ThroughComponent, IsDirectStyledRoute = Direct, DirectStyle = Style };
        path.Segments.AddRange(Segments.Select(segment => segment.Build()));
        return path;
    }
}

internal readonly record struct RouteSegmentValue(bool Bend, double StartX, double StartY, double EndX,
    double EndY, double StartAngle, double EndAngle, double CenterX, double CenterY, double Radius, double Sweep)
{
    internal static RouteSegmentValue Capture(PathSegment segment)
    {
        if (segment is not StraightSegment and not BendSegment)
            throw new NotSupportedException($"Routing snapshot cannot capture segment type '{segment.GetType().Name}'.");
        var bend = segment as BendSegment;
        return new(bend != null, segment.StartPoint.X, segment.StartPoint.Y, segment.EndPoint.X,
            segment.EndPoint.Y, segment.StartAngleDegrees, segment.EndAngleDegrees,
            bend?.Center.X ?? 0, bend?.Center.Y ?? 0, bend?.RadiusMicrometers ?? 0, bend?.SweepAngleDegrees ?? 0);
    }

    internal PathSegment Build()
    {
        PathSegment segment = Bend ? new BendSegment(CenterX, CenterY, Radius, StartAngle, Sweep)
            : new StraightSegment(StartX, StartY, EndX, EndY, StartAngle);
        segment.StartPoint = (StartX, StartY);
        segment.EndPoint = (EndX, EndY);
        segment.EndAngleDegrees = EndAngle;
        return segment;
    }
}

internal sealed record PolygonValue(int Layer, int DataType, ImmutableArray<OutlinePoint> Points)
{
    internal static ImmutableArray<PolygonValue> Capture(IReadOnlyList<OutlinePolygon>? polygons) =>
        polygons?.Select(polygon => new PolygonValue(polygon.Layer, polygon.DataType, polygon.Points.ToImmutableArray()))
            .ToImmutableArray() ?? ImmutableArray<PolygonValue>.Empty;
    internal OutlinePolygon Build() => new() { Layer = Layer, DataType = DataType, Points = Points.ToArray() };
    internal static AsDrawnGeometry? BuildDrawn(ImmutableArray<PolygonValue> polygons) =>
        polygons.IsEmpty ? null : new AsDrawnGeometry(polygons.Select(polygon => polygon.Build()).ToArray());
}

/// <summary>Routing evaluates losses at its existing 1550 nm reference; no live model crosses the boundary.</summary>
internal sealed record RoutingDispersionValue(double EffectiveIndex, double GroupIndex, double Loss)
{
    internal static RoutingDispersionValue? Capture(IDispersionModel? model) => model == null ? null
        : new(model.NEffAt(1550), model.GroupIndexAt(1550), model.LossDbPerCmAt(1550));
    internal IDispersionModel Build() => new ConstantDispersion(EffectiveIndex, GroupIndex, Loss);
}

internal sealed record ImportedRouteValue(ImmutableArray<PolygonValue> Polygons, RouteShapeSignature? Signature,
    RouteGeometryValue? OriginalPath, bool Displaced)
{
    internal static ImportedRouteValue Capture(WaveguideConnection connection)
    {
        var state = connection.CaptureImportedRouteState();
        return new(PolygonValue.Capture(state.Geometry?.Polygons), state.Signature,
            RouteGeometryValue.Capture(state.OriginalPath), state.Displaced);
    }
    internal void Restore(WaveguideConnection connection) => connection.RestoreImportedRouteState(
        PolygonValue.BuildDrawn(Polygons), Signature, OriginalPath?.Build(), Displaced);
}
