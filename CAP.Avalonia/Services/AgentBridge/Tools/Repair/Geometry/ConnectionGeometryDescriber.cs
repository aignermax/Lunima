using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_Core.Routing.InterconnectRouting.SegmentShift;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Geometry;

/// <summary>A point in µm.</summary>
public sealed record PointInfo(double X, double Y);

/// <summary>One path segment: a straight (with its straight_index for shift_segment) or an arc.</summary>
public sealed record SegmentInfo(
    string Type, PointInfo Start, PointInfo End, double LengthUm, double StartAngle, double EndAngle,
    int? StraightIndex, bool? Shiftable, PointInfo? Center, double? RadiusUm, double? SweepDegrees);

/// <summary>A pin: <c>component.pin</c>, absolute position and facing angle (0 = +x, 90 = +y).</summary>
public sealed record PinInfo(string Pin, PointInfo Position, double AngleDegrees);

/// <summary>
/// Maps a connection's routed path and pins to agent-facing records. For a blocked wire the
/// path is the red placeholder (a straight line between short pin leads), not a real route.
/// </summary>
public static class ConnectionGeometryDescriber
{
    private const int Decimals = 3;

    /// <summary>The segments of <paramref name="connection"/>, straights numbered as <c>shift_segment</c> expects.</summary>
    public static IReadOnlyList<SegmentInfo> Segments(WaveguideConnection connection)
    {
        var segments = connection.GetPathSegments();
        var result = new List<SegmentInfo>(segments.Count);
        var straightIndex = -1;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (segment is StraightSegment)
                straightIndex++;
            result.Add(Describe(segment, segments, i, straightIndex));
        }
        return result;
    }

    /// <summary>Describes a pin.</summary>
    public static PinInfo Pin(PhysicalPin pin)
    {
        var (x, y) = pin.GetAbsolutePosition();
        return new PinInfo($"{pin.ParentComponent.Identifier}.{pin.Name}", Point(x, y), Round(pin.GetAbsoluteAngle()));
    }

    /// <summary>Rounds a point.</summary>
    public static PointInfo Point(double x, double y) => new(Round(x), Round(y));

    /// <summary>Rounds to <see cref="Decimals"/> places.</summary>
    public static double Round(double value) => Math.Round(value, Decimals);

    /// <summary>Shortest distance between any sampled point of the two paths (µm), or null when either has no path.</summary>
    public static double? Distance(WaveguideConnection a, WaveguideConnection b)
    {
        var pa = Samples(a).ToList();
        var pb = Samples(b).ToList();
        if (pa.Count == 0 || pb.Count == 0)
            return null;
        return pa.Min(p => pb.Min(q => Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y))));
    }

    /// <summary>Points along the path every few µm (segment ends included).</summary>
    public static IEnumerable<(double X, double Y)> Samples(WaveguideConnection connection)
    {
        const double step = 5.0;
        foreach (var segment in connection.GetPathSegments())
        {
            var (sx, sy) = segment.StartPoint;
            var (ex, ey) = segment.EndPoint;
            var count = Math.Max(1, (int)Math.Ceiling(segment.LengthMicrometers / step));
            for (var k = 0; k <= count; k++)
            {
                var t = (double)k / count;
                yield return (sx + (ex - sx) * t, sy + (ey - sy) * t);
            }
        }
    }

    private static SegmentInfo Describe(PathSegment segment, IReadOnlyList<PathSegment> all, int index, int straightIndex)
    {
        var start = Point(segment.StartPoint.X, segment.StartPoint.Y);
        var end = Point(segment.EndPoint.X, segment.EndPoint.Y);
        if (segment is BendSegment bend)
            return new SegmentInfo("arc", start, end, Round(bend.LengthMicrometers), Round(bend.StartAngleDegrees),
                Round(bend.EndAngleDegrees), null, null, Point(bend.Center.X, bend.Center.Y),
                Round(bend.RadiusMicrometers), Round(bend.SweepAngleDegrees));
        return new SegmentInfo("straight", start, end, Round(segment.LengthMicrometers), Round(segment.StartAngleDegrees),
            Round(segment.EndAngleDegrees), straightIndex, SegmentShiftGeometry.IsShiftable(all, index), null, null, null);
    }
}
