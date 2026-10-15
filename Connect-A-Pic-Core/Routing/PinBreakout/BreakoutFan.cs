namespace CAP_Core.Routing.PinBreakout;

/// <summary>
/// The breakout geometry of one cluster for a monotone side assignment
/// (<see cref="BreakoutSideSelector"/>). Every wire first leaves its pin straight through the
/// component's clearance band (the escape run), exactly like a grid route does. Wires turning
/// towards a side then leave in order from that side: the pin nearest the side turns first,
/// every next one runs one routable pitch further out, so the arcs are translated copies and
/// the lanes are one pitch apart. The straight wires in the middle spread to the routable
/// pitch with S-bends, centered on their own group, and end on one common anchor line.
/// <code>
///   side −1:  ╭── lane        straight:  ──╮╭── anchor      side +1:  ── … ╮
///   pin ──────╯                pin ────────╯╰──             pin ─╮       lane
/// </code>
/// </summary>
public sealed class BreakoutFan
{
    private const double DegreesToRadians = Math.PI / 180.0;

    private readonly PinCluster _cluster;
    private readonly double _radius;
    private readonly double _pitch;
    private readonly double _stub;
    private readonly double _escape;
    private readonly List<double> _lateral;

    /// <summary>Creates the fan of <paramref name="cluster"/>.</summary>
    /// <param name="cluster">The cluster (pins ordered by lateral position).</param>
    /// <param name="radius">Arc radius (µm).</param>
    /// <param name="routablePitch">Lane pitch (µm) the wires leave at.</param>
    /// <param name="stub">Straight run (µm) between the last arc and a straight wire's anchor.</param>
    /// <param name="escape">Straight run (µm) out of the pin before any arc.</param>
    public BreakoutFan(PinCluster cluster, double radius, double routablePitch, double stub, double escape)
    {
        _cluster = cluster;
        _radius = radius;
        _pitch = routablePitch;
        _stub = stub;
        _escape = escape;
        _lateral = cluster.Pins.Select(p =>
        {
            var (x, y) = p.GetAbsolutePosition();
            return PinClusterDetector.Lateral(x, y, cluster.HeadingDegrees);
        }).ToList();
    }

    /// <summary>Lateral pitch (µm) between pin <paramref name="k"/> and the pin before it.</summary>
    public double PitchBefore(int k) => _lateral[k] - _lateral[k - 1];

    /// <summary>
    /// Builds pin <paramref name="k"/>'s breakout (pin → anchor) for the given sides.
    /// <paramref name="extraRun"/> (µm) moves every arc and anchor that much further out of
    /// the face — the planner uses it to land the lanes on free grid cells.
    /// </summary>
    public List<PathSegment> Build(int k, IReadOnlyList<int> sides, double extraRun = 0)
    {
        var (pinX, pinY) = _cluster.Pins[k].GetAbsolutePosition();
        double heading = _cluster.HeadingDegrees;
        double rad = heading * DegreesToRadians;
        double x = pinX + _escape * Math.Cos(rad);
        double y = pinY + _escape * Math.Sin(rad);
        var segments = new List<PathSegment> { new StraightSegment(pinX, pinY, x, y, heading) };

        int side = sides[k];
        if (side != 0)
        {
            int rank = side > 0 ? CountFrom(sides, k + 1, side) : CountUpTo(sides, k - 1, side);
            segments.AddRange(BreakoutQuarterTurn.Build(x, y, heading, extraRun + rank * _pitch, side, _radius));
            return segments;
        }

        var straight = Enumerable.Range(0, sides.Count).Where(i => sides[i] == 0).ToList();
        int index = straight.IndexOf(k);
        double center = (_lateral[straight[0]] + _lateral[straight[^1]]) / 2;
        double Offset(int j) => center + (j - (straight.Count - 1) / 2.0) * _pitch - _lateral[straight[j]];
        double forward = Enumerable.Range(0, straight.Count)
            .Max(j => BreakoutSBend.ForwardLength(Offset(j), _radius)) + _stub + extraRun;
        segments.AddRange(BreakoutSBend.Build(x, y, heading, Offset(index), _radius, forward));
        return segments;
    }

    /// <summary>Entries from <paramref name="start"/> on that equal <paramref name="side"/>.</summary>
    private static int CountFrom(IReadOnlyList<int> sides, int start, int side)
    {
        int count = 0;
        for (int i = start; i < sides.Count; i++)
            if (sides[i] == side) count++;
        return count;
    }

    /// <summary>Entries up to <paramref name="end"/> (inclusive) that equal <paramref name="side"/>.</summary>
    private static int CountUpTo(IReadOnlyList<int> sides, int end, int side)
    {
        int count = 0;
        for (int i = 0; i <= end; i++)
            if (sides[i] == side) count++;
        return count;
    }
}
