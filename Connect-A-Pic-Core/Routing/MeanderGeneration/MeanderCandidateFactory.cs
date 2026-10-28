namespace CAP_Core.Routing.MeanderGeneration;

/// <summary>
/// One candidate route: the full path plus the pieces of it that are new geometry (judged
/// against obstacles). <see cref="RebuiltPinToPin"/> marks a path rebuilt from the pins, whose
/// new geometry ends at the pins themselves and therefore earns the fan-out exemption.
/// </summary>
internal sealed record MeanderCandidate(RoutedPath Path, IReadOnlyList<RoutedPath> ChangedPieces, bool RebuiltPinToPin);

/// <summary>
/// Enumerates meander candidates for a host route, most compact first: every long straight can
/// host the fold, on either side, with several loop counts and at the center, start or end of
/// the straight; finally the extra length is split across the two longest straights. The first
/// candidate is the classic one (longest straight, max loops, centered, first side), so an
/// unobstructed route gets exactly the geometry it always got. Deterministic.
/// </summary>
internal static class MeanderCandidateFactory
{
    private const double GeometrySlackMicrometers = 1e-6;
    private const int MaxLoopCount = 4096;
    private const int MaxHostStraights = 6;
    private const int BisectionIterations = 60;
    private static readonly double[] Sides = [1.0, -1.0];
    private static readonly double[] Placements = [0.5, 0.0, 1.0];

    private sealed record Fold(int Host, double Footprint, double Placement, Action<PosePathBuilder> Emit);

    /// <summary>Candidates absorbing <paramref name="extraMicrometers"/> into <paramref name="basePath"/>.</summary>
    public static IEnumerable<MeanderCandidate> Enumerate(
        RoutedPath basePath, bool rebuiltPinToPin, double extraMicrometers, double radius)
    {
        var hosts = HostStraights(basePath);
        foreach (int host in hosts)
        {
            double hostLength = basePath.Segments[host].LengthMicrometers;
            foreach (var (footprint, emit) in Folds(hostLength, extraMicrometers, radius))
            foreach (double side in Sides)
            foreach (double placement in Placements)
                yield return Assemble(basePath, rebuiltPinToPin,
                    [new Fold(host, footprint, placement, builder => emit(builder, side))]);
        }

        if (hosts.Count < 2)
            yield break;
        foreach (var split in SplitAcrossTwoHosts(basePath, hosts[0], hosts[1], extraMicrometers, radius))
            yield return Assemble(basePath, rebuiltPinToPin, split);
    }

    private static List<int> HostStraights(RoutedPath path) => Enumerable.Range(0, path.Segments.Count)
        .Where(i => path.Segments[i] is StraightSegment s && s.LengthMicrometers > GeometrySlackMicrometers)
        .OrderByDescending(i => path.Segments[i].LengthMicrometers)
        .ThenBy(i => i)
        .Take(MaxHostStraights)
        .ToList();

    /// <summary>Folds that add exactly <paramref name="extra"/> and fit along a host of the given length.</summary>
    private static IEnumerable<(double Footprint, Action<PosePathBuilder, double> Emit)> Folds(
        double hostLength, double extra, double radius)
    {
        double loopMinExtra = (2.0 * Math.PI - 4.0) * radius;
        if (extra >= loopMinExtra)
        {
            int byLength = (int)Math.Floor(extra / loopMinExtra);
            int byRoom = (int)Math.Floor((hostLength + GeometrySlackMicrometers) / (4.0 * radius));
            foreach (int loops in LoopCountVariants(Math.Min(Math.Min(byLength, byRoom), MaxLoopCount)))
            {
                double height = (extra / loops - loopMinExtra) / 2.0;
                yield return (loops * 4.0 * radius, (builder, side) => EmitLoops(builder, loops, height, radius, side));
            }
        }

        // Extra smaller than one hairpin loop: a double S-bend leaves the host axis and returns,
        // absorbing exactly 4r·(sweep − sin sweep), which reaches loopMinExtra at 90°.
        if (extra > loopMinExtra)
            yield break;
        double sweepDeg = SolveDoubleSBendSweepDegrees(extra, radius);
        double footprint = 4.0 * radius * Math.Sin(sweepDeg * Math.PI / 180.0);
        if (footprint > hostLength + GeometrySlackMicrometers)
            yield break;
        yield return (footprint, (builder, side) =>
        {
            builder.AppendArc(sweepDeg * side, radius);
            builder.AppendArc(-sweepDeg * side, radius);
            builder.AppendArc(-sweepDeg * side, radius);
            builder.AppendArc(sweepDeg * side, radius);
        });
    }

    /// <summary>Most loops (lowest lateral reach) first, then progressively fewer, shorter folds.</summary>
    private static IEnumerable<int> LoopCountVariants(int maxLoops)
    {
        if (maxLoops < 1)
            return [];
        return new[] { maxLoops, maxLoops - 1, maxLoops - 2, (maxLoops + 1) / 2, 1 }.Where(n => n >= 1).Distinct();
    }

    private static IEnumerable<IReadOnlyList<Fold>> SplitAcrossTwoHosts(
        RoutedPath basePath, int first, int second, double extra, double radius)
    {
        double firstLength = basePath.Segments[first].LengthMicrometers;
        double secondLength = basePath.Segments[second].LengthMicrometers;
        double firstExtra = extra * firstLength / (firstLength + secondLength);
        var firstFold = Folds(firstLength, firstExtra, radius).FirstOrDefault();
        var secondFold = Folds(secondLength, extra - firstExtra, radius).FirstOrDefault();
        if (firstFold.Emit == null || secondFold.Emit == null)
            yield break;
        foreach (double firstSide in Sides)
        foreach (double secondSide in Sides)
            yield return
            [
                new Fold(first, firstFold.Footprint, 0.5, builder => firstFold.Emit(builder, firstSide)),
                new Fold(second, secondFold.Footprint, 0.5, builder => secondFold.Emit(builder, secondSide)),
            ];
    }

    private static MeanderCandidate Assemble(RoutedPath basePath, bool rebuiltPinToPin, IReadOnlyList<Fold> folds)
    {
        var result = new RoutedPath();
        var changed = new List<RoutedPath>();
        for (int i = 0; i < basePath.Segments.Count; i++)
        {
            var fold = folds.FirstOrDefault(f => f.Host == i);
            if (fold == null)
            {
                result.Segments.Add(basePath.Segments[i]);
                continue;
            }
            int firstNew = result.Segments.Count;
            AppendFold(result, basePath.Segments[i], fold);
            var piece = new RoutedPath();
            piece.Segments.AddRange(result.Segments.Skip(firstNew));
            changed.Add(piece);
        }
        return new MeanderCandidate(result, rebuiltPinToPin ? [result] : changed, rebuiltPinToPin);
    }

    private static void AppendFold(RoutedPath result, PathSegment host, Fold fold)
    {
        double dirX = host.EndPoint.X - host.StartPoint.X;
        double dirY = host.EndPoint.Y - host.StartPoint.Y;
        double hostLength = Math.Sqrt(dirX * dirX + dirY * dirY);
        double headingDeg = Math.Atan2(dirY, dirX) * 180.0 / Math.PI;
        double prefix = Math.Max(0.0, hostLength - fold.Footprint) * fold.Placement;

        var builder = new PosePathBuilder(result, host.StartPoint.X, host.StartPoint.Y, headingDeg);
        builder.AppendStraight(prefix);
        fold.Emit(builder);
        builder.AppendStraight(hostLength - prefix - fold.Footprint);
    }

    private static void EmitLoops(PosePathBuilder builder, int loops, double heightMicrometers, double radius, double side)
    {
        for (int i = 0; i < loops; i++)
        {
            builder.AppendArc(90.0 * side, radius);
            builder.AppendStraight(heightMicrometers);
            builder.AppendArc(-180.0 * side, radius);
            builder.AppendStraight(heightMicrometers);
            builder.AppendArc(90.0 * side, radius);
        }
    }

    /// <summary>
    /// Sweep for the four arcs of a double S-bend so that its length exceeds the chord it
    /// replaces by exactly <paramref name="extraMicrometers"/>: extra = 4r·(sweep − sin sweep)
    /// is strictly monotone, so bisection converges.
    /// </summary>
    private static double SolveDoubleSBendSweepDegrees(double extraMicrometers, double radius)
    {
        double lo = 0.0;
        double hi = Math.PI / 2.0;
        for (int i = 0; i < BisectionIterations; i++)
        {
            double mid = (lo + hi) / 2.0;
            if (4.0 * radius * (mid - Math.Sin(mid)) < extraMicrometers)
                lo = mid;
            else
                hi = mid;
        }
        return (lo + hi) / 2.0 * 180.0 / Math.PI;
    }
}
