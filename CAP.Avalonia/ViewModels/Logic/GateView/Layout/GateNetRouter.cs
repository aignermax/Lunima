using Avalonia;

namespace CAP.Avalonia.ViewModels.Logic.GateView.Layout;

/// <summary>
/// Draws one driver→load wire of a net as a clean orthogonal polyline: out of the driver
/// along the net's trunk, into the load from the left. A wire whose short route would cut
/// through another gate symbol (or that runs backwards, a feedback loop) takes a channel
/// just above or below the load's row instead. Runs claim tracks in a
/// <see cref="GateTrackRegistry"/>, so parallel wires never lie on top of each other.
/// This is a readability heuristic, not a schematic router — the physical waveguides
/// remain the truth in the physical view.
/// </summary>
public static class GateNetRouter
{
    /// <summary>Lane step as a share of the base clearance.</summary>
    public const double LaneStepShare = 0.5;

    /// <summary>Smallest lane step in µm, so neighbouring wires of small symbols still read apart.</summary>
    public const double MinLaneStep = 24;

    /// <summary>Spacing of parallel wires for a given base clearance.</summary>
    public static double LaneStep(double gap) => Math.Max(gap * LaneStepShare, MinLaneStep);

    private const double ChannelShare = 0.55;

    /// <summary>Extra clearance below a symbol, where its name label sits.</summary>
    private const double BelowClearanceFactor = 2.0;

    /// <summary>Wires touching a footprint edge (pin stubs end there) do not count as crossing it.</summary>
    private const double EdgeTolerance = 0.5;

    /// <summary>One wire to route.</summary>
    /// <param name="Source">Free end of the driving output stub.</param>
    /// <param name="Target">Free end of the driven input stub.</param>
    /// <param name="TargetFootprint">The driven symbol's footprint.</param>
    /// <param name="Gap">Base clearance in µm (the driver's stub length).</param>
    /// <param name="TrunkX">The x of the net's vertical trunk right of the driver.</param>
    public readonly record struct Request(Point Source, Point Target, Rect TargetFootprint, double Gap, double TrunkX);

    /// <summary>The preferred trunk x right of a driver pin.</summary>
    public static double PreferredTrunkX(Point source, double gap) => source.X + gap * 0.5;

    /// <summary>Routes one wire around the given obstacles (symbol footprints), claiming its tracks.</summary>
    internal static Point[] Route(Request request, IReadOnlyList<Rect> obstacles, GateTrackRegistry tracks)
    {
        var (s, t) = (request.Source, request.Target);
        var step = LaneStep(request.Gap);
        var xs = request.TrunkX;
        var baseXt = t.X - request.Gap * 0.5;

        if (baseXt > xs)
        {
            Point[] nearSource = [s, new(xs, s.Y), new(xs, t.Y), t];
            if (IsClear(nearSource, obstacles))
            {
                tracks.AddHorizontal(t.Y, xs, t.X);
                return nearSource;
            }
            var xt = tracks.FindVertical(baseXt, s.Y, t.Y, step, -1);
            Point[] nearTarget = [s, new(xt, s.Y), new(xt, t.Y), t];
            if (xt > xs && IsClear(nearTarget, obstacles))
            {
                tracks.AddVertical(xt, s.Y, t.Y);
                tracks.AddHorizontal(s.Y, s.X, xt);
                return nearTarget;
            }
        }
        return ChannelRoute(request, xs, baseXt, step, obstacles, tracks);
    }

    /// <summary>
    /// Leaves the driver down its trunk, runs along a channel just above or below the
    /// load's row, and drops into the load from its left.
    /// </summary>
    private static Point[] ChannelRoute(
        Request request, double xs, double baseXt, double step, IReadOnlyList<Rect> obstacles, GateTrackRegistry tracks)
    {
        var (s, t) = (request.Source, request.Target);
        // Same-row and downward wires prefer the channel above the load: below it sits the gate name.
        var preferAbove = t.Y >= s.Y - request.TargetFootprint.Height;
        var preferred = Channel(request, xs, baseXt, step, tracks, above: preferAbove);
        if (IsClear(preferred.Route, obstacles))
            return Commit(preferred, tracks);
        var alternative = Channel(request, xs, baseXt, step, tracks, above: !preferAbove);
        return Commit(IsClear(alternative.Route, obstacles) ? alternative : preferred, tracks);
    }

    private readonly record struct ChannelPlan(Point[] Route, double Y, double Xt);

    private static ChannelPlan Channel(Request request, double xs, double baseXt, double step, GateTrackRegistry tracks, bool above)
    {
        var (s, t) = (request.Source, request.Target);
        var clearance = request.Gap * ChannelShare;
        var baseY = above
            ? request.TargetFootprint.Top - clearance
            : request.TargetFootprint.Bottom + clearance * BelowClearanceFactor;
        var y = tracks.FindHorizontal(baseY, Math.Min(xs, baseXt), Math.Max(xs, baseXt), step, above ? -1 : 1);
        var xt = tracks.FindVertical(baseXt, y, t.Y, step, -1);
        return new ChannelPlan([s, new(xs, s.Y), new(xs, y), new(xt, y), new(xt, t.Y), t], y, xt);
    }

    private static Point[] Commit(ChannelPlan plan, GateTrackRegistry tracks)
    {
        var route = plan.Route;
        tracks.AddHorizontal(plan.Y, route[2].X, plan.Xt);
        tracks.AddVertical(plan.Xt, plan.Y, route[^1].Y);
        return route;
    }

    /// <summary>True when no segment of the polyline passes through an obstacle's interior.</summary>
    public static bool IsClear(IReadOnlyList<Point> polyline, IReadOnlyList<Rect> obstacles)
    {
        for (var i = 1; i < polyline.Count; i++)
        {
            foreach (var obstacle in obstacles)
            {
                if (Crosses(polyline[i - 1], polyline[i], obstacle))
                    return false;
            }
        }
        return true;
    }

    /// <summary>Axis-aligned segment against a rectangle's open interior.</summary>
    private static bool Crosses(Point a, Point b, Rect obstacle)
    {
        var r = obstacle.Deflate(EdgeTolerance);
        if (Math.Abs(a.Y - b.Y) < double.Epsilon)
        {
            return a.Y > r.Top && a.Y < r.Bottom
                   && Math.Max(a.X, b.X) > r.Left && Math.Min(a.X, b.X) < r.Right;
        }
        return a.X > r.Left && a.X < r.Right
               && Math.Max(a.Y, b.Y) > r.Top && Math.Min(a.Y, b.Y) < r.Bottom;
    }
}
