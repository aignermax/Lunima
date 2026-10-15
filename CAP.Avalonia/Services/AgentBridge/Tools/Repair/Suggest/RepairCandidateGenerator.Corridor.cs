using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_Core.Routing.InterconnectRouting.SegmentShift;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;

/// <summary>
/// Corridor geometry for the widening candidates: which neighbouring straights run past the
/// blocked wire's pins, and which direction of a shift or a move opens the corridor in front
/// of a pin instead of narrowing it.
/// </summary>
public sealed partial class RepairCandidateGenerator
{
    /// <summary>
    /// Straights of any wire this close (µm) to a pin of the blocked wire bound the corridor in
    /// front of it and are shift candidates, even when that wire's own pins are far away.
    /// </summary>
    public const double CorridorRadiusMicrometers = 40;

    /// <summary>Distance from the straight behind <paramref name="handle"/> to the nearer pin of the blocked wire.</summary>
    private double HandleDistanceToPins(WaveguideConnection connection, StraightSegmentHandle handle)
    {
        var straight = StraightOf(connection, handle);
        if (straight == null)
            return DistanceToPins(handle.Midpoint);
        return BlockedPins().Min(pin => DistanceToSegment(pin, straight));
    }

    /// <summary>
    /// +1 when a positive shift (along the handle's normal) moves the straight away from the
    /// nearer pin of the blocked wire, −1 otherwise — the direction that widens the corridor.
    /// </summary>
    private int WideningSign(WaveguideConnection connection, StraightSegmentHandle handle)
    {
        var straight = StraightOf(connection, handle);
        var pins = BlockedPins();
        var pin = straight == null
            ? pins.OrderBy(p => Hypot(p.X - handle.Midpoint.X, p.Y - handle.Midpoint.Y)).First()
            : pins.OrderBy(p => DistanceToSegment(p, straight)).First();
        var (cx, cy) = straight == null ? handle.Midpoint : ClosestPoint(pin, straight);
        double away = (cx - pin.X) * handle.Normal.X + (cy - pin.Y) * handle.Normal.Y;
        return away >= 0 ? 1 : -1;
    }

    /// <summary>
    /// The along-pin moves of the end components, interleaved across the ends: first moving
    /// each component back from its pin's facing direction (the channel in front of the pin
    /// widens), smallest step first, then towards it.
    /// </summary>
    private static IEnumerable<(PhysicalPin Pin, ComponentViewModel Vm, double Step)> WideningAxisSteps(
        IReadOnlyList<(PhysicalPin Pin, ComponentViewModel? Vm)> ends)
    {
        var series = ends
            .Where(e => e.Vm != null)
            .Select(e => (e.Pin, Vm: e.Vm!, Steps: CorridorSteps.WideningFirst(BackFromPinSign(e.Pin))))
            .ToList();
        int longest = series.Count == 0 ? 0 : series.Max(s => s.Steps.Count);
        for (int i = 0; i < longest; i++)
            foreach (var (pin, vm, steps) in series.Where(s => i < s.Steps.Count))
                yield return (pin, vm, steps[i]);
    }

    /// <summary>Sign of the along-pin move (see <see cref="AlongPin"/>) that moves the component away from where its pin faces.</summary>
    private static int BackFromPinSign(PhysicalPin pin)
    {
        var radians = pin.GetAbsoluteAngle() * Math.PI / 180.0;
        double cos = Math.Cos(radians), sin = Math.Sin(radians);
        double facing = Math.Abs(cos) >= Math.Abs(sin) ? cos : sin;
        return facing > 0 ? -1 : 1;
    }

    private (double X, double Y)[] BlockedPins() =>
        new[] { _blocked.StartPin.GetAbsolutePosition(), _blocked.EndPin.GetAbsolutePosition() };

    private static StraightSegment? StraightOf(WaveguideConnection connection, StraightSegmentHandle handle) =>
        connection.GetPathSegments().OfType<StraightSegment>().ElementAtOrDefault(handle.StraightIndex);

    private static double DistanceToSegment((double X, double Y) point, StraightSegment segment)
    {
        var (x, y) = ClosestPoint(point, segment);
        return Hypot(point.X - x, point.Y - y);
    }

    private static (double X, double Y) ClosestPoint((double X, double Y) point, StraightSegment segment)
    {
        var (ax, ay) = segment.StartPoint;
        var (bx, by) = segment.EndPoint;
        double dx = bx - ax, dy = by - ay, lengthSquared = dx * dx + dy * dy;
        double t = lengthSquared < 1e-12 ? 0 : Math.Clamp(((point.X - ax) * dx + (point.Y - ay) * dy) / lengthSquared, 0, 1);
        return (ax + t * dx, ay + t * dy);
    }
}
