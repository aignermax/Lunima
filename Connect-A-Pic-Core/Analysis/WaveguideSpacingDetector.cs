using System.Globalization;
using CAP_Core.Analysis.Hierarchy;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Component = CAP_Core.Components.Core.Component;

namespace CAP_Core.Analysis;

/// <summary>
/// Detects waveguide route segments whose edge-to-edge distance is below a
/// process-defined minimum spacing, or whose process keep-out bands overlap. Uses a
/// uniform spatial grid so large imported designs stay responsive.
/// </summary>
public class WaveguideSpacingDetector
{
    private readonly record struct SegmentInfo(
        Guid PathId,
        string PathLabel,
        WaveguideConnection? Connection,
        PathSegment Segment,
        int SegmentIndex,
        double HalfWidthMicrometers,
        double SpacingMicrometers,
        double KeepOutMicrometers);

    /// <summary>
    /// Detects all distinct waveguide segment pairs whose edge-to-edge clearance
    /// is smaller than the governing minimum spacing. Without a per-connection
    /// provider that is <paramref name="minWaveguideSpacingMicrometers"/> for every
    /// segment; with one (issue #936) each connection's segments carry the minimum of
    /// their own endpoint PDKs' processes (≤0 = no declared limit) while frozen group
    /// paths keep <paramref name="minWaveguideSpacingMicrometers"/>, and a pair is
    /// governed by the stricter (larger) of its two segments' values — so a
    /// Cornerstone route enforces its foundry gap even against a SiEPIC neighbour
    /// that declares no limit of its own.
    /// When a keep-out provider is given, two wires whose keep-out bands overlap are
    /// flagged too: the pair needs an edge gap of at least the sum of both keep-out
    /// growths. Same-wire pairs, segments sharing an endpoint and pairs at a crossing of
    /// the two wires stay silent (see <see cref="WaveguideKeepOutCrossingExemption"/>).
    /// </summary>
    /// <param name="connections">Regular waveguide connections to check.</param>
    /// <param name="groups">ComponentGroups whose frozen internal paths are checked.</param>
    /// <param name="minWaveguideSpacingMicrometers">
    /// Minimum required edge-to-edge spacing for frozen group paths and — when no
    /// per-connection provider is given — for every connection.
    /// </param>
    /// <param name="spacingForConnection">
    /// Optional per-connection minimum spacing resolver (issue #936); a return ≤0
    /// means the connection's PDKs declare no limit. Null keeps the uniform
    /// <paramref name="minWaveguideSpacingMicrometers"/> behavior.
    /// </param>
    /// <param name="keepOutForConnection">
    /// Optional per-connection keep-out growth beyond each core edge in µm (0 = none).
    /// Frozen group paths carry no keep-out.
    /// </param>
    /// <returns>A list of spacing design issues, empty if none found.</returns>
    public List<DesignIssue> DetectViolations(
        IEnumerable<WaveguideConnection> connections,
        IEnumerable<ComponentGroup> groups,
        double minWaveguideSpacingMicrometers,
        Func<WaveguideConnection, double>? spacingForConnection = null,
        Func<WaveguideConnection, double>? keepOutForConnection = null)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(groups);

        var segments = CollectSegments(
            connections, groups, minWaveguideSpacingMicrometers, spacingForConnection, keepOutForConnection);
        if (segments.Count < 2)
            return new List<DesignIssue>();

        double maxKeepOut = segments.Max(s => s.KeepOutMicrometers);
        double maxReach = segments.Max(s => Reach(s, maxKeepOut));
        if (maxReach <= 0)
            return new List<DesignIssue>();

        var pairs = FindCandidatePairs(segments, maxReach, maxKeepOut);
        var crossings = new WaveguideKeepOutCrossingExemption(segments.Select(s => (s.PathId, s.Segment)));
        var issues = new List<DesignIssue>();

        foreach (var (indexA, indexB) in pairs)
        {
            var issue = CheckPair(segments[indexA], segments[indexB], crossings);
            if (issue is not null)
                issues.Add(issue);
        }

        return issues;
    }

    /// <summary>
    /// How far beyond its core edge a segment can take part in a finding: its own
    /// spacing, or its keep-out plus the widest keep-out any partner can bring.
    /// </summary>
    private static double Reach(SegmentInfo segment, double maxKeepOut) =>
        Math.Max(segment.SpacingMicrometers, segment.KeepOutMicrometers + maxKeepOut);

    private static List<SegmentInfo> CollectSegments(
        IEnumerable<WaveguideConnection> connections,
        IEnumerable<ComponentGroup> groups,
        double minWaveguideSpacingMicrometers,
        Func<WaveguideConnection, double>? spacingForConnection,
        Func<WaveguideConnection, double>? keepOutForConnection)
    {
        var segments = new List<SegmentInfo>();

        foreach (var connection in connections)
        {
            if (connection.RoutedPath?.Segments is not { Count: > 0 } routedSegments)
                continue;

            var label = FormatConnectionLabel(connection);
            double halfWidth = connection.WidthMicrometers / 2.0;
            double spacing = spacingForConnection?.Invoke(connection) ?? minWaveguideSpacingMicrometers;
            double keepOut = Math.Max(0.0, keepOutForConnection?.Invoke(connection) ?? 0.0);

            for (int i = 0; i < routedSegments.Count; i++)
            {
                segments.Add(new SegmentInfo(
                    connection.Id, label, connection, routedSegments[i], i, halfWidth, spacing, keepOut));
            }
        }

        foreach (var hierarchical in DesignHierarchy.From(groups.Cast<Component>()).FrozenPaths)
        {
            var frozen = hierarchical.Frozen;
            if (frozen.Path?.Segments is not { Count: > 0 } frozenSegments)
                continue;

            var label = $"Group '{hierarchical.GroupPath}' frozen path";
            double halfWidth = frozen.WidthMicrometers / 2.0;

            for (int i = 0; i < frozenSegments.Count; i++)
            {
                segments.Add(new SegmentInfo(
                    frozen.PathId,
                    label,
                    null,
                    frozenSegments[i],
                    i,
                    halfWidth,
                    minWaveguideSpacingMicrometers,
                    0.0));
            }
        }

        return segments;
    }

    private static HashSet<(int IndexA, int IndexB)> FindCandidatePairs(
        List<SegmentInfo> segments, double maxReach, double maxKeepOut)
    {
        double maxHalfWidth = segments.Max(s => s.HalfWidthMicrometers);
        double cellSize = maxReach + maxHalfWidth * 2.0;

        var buckets = new Dictionary<(int X, int Y), List<int>>();

        for (int i = 0; i < segments.Count; i++)
        {
            var (bx0, by0, bx1, by1) = BucketRange(segments[i], cellSize, maxKeepOut, margin: 0);
            for (int bx = bx0; bx <= bx1; bx++)
            {
                for (int by = by0; by <= by1; by++)
                {
                    if (!buckets.TryGetValue((bx, by), out var list))
                    {
                        list = new List<int>();
                        buckets[(bx, by)] = list;
                    }

                    list.Add(i);
                }
            }
        }

        var pairs = new HashSet<(int, int)>();

        for (int i = 0; i < segments.Count; i++)
        {
            var (bx0, by0, bx1, by1) = BucketRange(segments[i], cellSize, maxKeepOut, margin: 1);
            for (int bx = bx0; bx <= bx1; bx++)
            {
                for (int by = by0; by <= by1; by++)
                {
                    if (!buckets.TryGetValue((bx, by), out var list))
                        continue;

                    foreach (int j in list)
                    {
                        if (j > i)
                            pairs.Add((i, j));
                    }
                }
            }
        }

        return pairs;
    }

    private static (int X0, int Y0, int X1, int Y1) BucketRange(
        SegmentInfo segment, double cellSize, double maxKeepOut, int margin)
    {
        var (minX, minY, maxX, maxY) = WaveguideSpacingGeometry.GetPaddedBounds(
            segment.Segment, segment.HalfWidthMicrometers, Reach(segment, maxKeepOut));
        return (
            (int)Math.Floor(minX / cellSize) - margin,
            (int)Math.Floor(minY / cellSize) - margin,
            (int)Math.Floor(maxX / cellSize) + margin,
            (int)Math.Floor(maxY / cellSize) + margin);
    }

    private static DesignIssue? CheckPair(
        SegmentInfo a, SegmentInfo b, WaveguideKeepOutCrossingExemption crossings)
    {
        if (a.PathId == b.PathId)
            return null;

        // The stricter side governs: the pair must respect the larger of the two
        // segments' process minima; a pair where neither side declares one is silent.
        double minSpacing = Math.Max(a.SpacingMicrometers, b.SpacingMicrometers);
        double keepOutGap = a.KeepOutMicrometers + b.KeepOutMicrometers;
        double required = Math.Max(minSpacing, keepOutGap);
        if (required <= 0)
            return null;

        if (WaveguideSpacingGeometry.SegmentsShareEndpoint(a.Segment, b.Segment))
            return null;

        var (centerDistance, closestPoint) = WaveguideSpacingGeometry.ComputeCenterlineDistance(
            a.Segment, b.Segment, required);

        if (centerDistance <= WaveguideSpacingGeometry.DistanceToleranceMicrometers)
            return null;

        double edgeDistance = Math.Max(centerDistance - a.HalfWidthMicrometers - b.HalfWidthMicrometers, 0.0);
        if (edgeDistance >= required - WaveguideSpacingGeometry.DistanceToleranceMicrometers)
            return null;

        bool violatesMinSpacing = edgeDistance < minSpacing - WaveguideSpacingGeometry.DistanceToleranceMicrometers;
        if (violatesMinSpacing)
            return CreateIssue(a, b, edgeDistance, minSpacing, closestPoint, keepOut: false);

        // Only the keep-out rule fires: two wires that cross each other necessarily
        // merge their bands around the crossing point, which is not a finding.
        double crossingRadius = required + a.HalfWidthMicrometers + b.HalfWidthMicrometers;
        if (crossings.IsNearCrossing(a.PathId, b.PathId, closestPoint, crossingRadius))
            return null;

        return CreateIssue(a, b, edgeDistance, required, closestPoint, keepOut: true);
    }

    private static DesignIssue CreateIssue(
        SegmentInfo a, SegmentInfo b, double edgeDistance, double required,
        (double X, double Y) at, bool keepOut)
    {
        string description = keepOut
            ? string.Create(CultureInfo.InvariantCulture,
                $"Keep-out bands overlap: {a.PathLabel} ↔ {b.PathLabel} (core gap {edgeDistance:F2} µm, keep-out needs {required:F2} µm)")
            : string.Create(CultureInfo.InvariantCulture,
                $"Waveguides too close: {a.PathLabel} ↔ {b.PathLabel} (distance {edgeDistance:F2} µm, minimum {required:F2} µm)");

        return new DesignIssue(
            DesignIssueType.WaveguideSpacingViolation,
            a.Connection ?? b.Connection,
            at.X,
            at.Y,
            description,
            localizationKey: keepOut ? "DesignChecks.WaveguideKeepOutOverlap" : "DesignChecks.WaveguideSpacingViolation",
            localizationArgs: new object[] { a.PathLabel, b.PathLabel, edgeDistance, required });
    }

    private static string FormatConnectionLabel(WaveguideConnection connection)
    {
        var start = $"{connection.StartPin.ParentComponent.Identifier}.{connection.StartPin.Name}";
        var end = $"{connection.EndPin.ParentComponent.Identifier}.{connection.EndPin.Name}";
        return $"{start} → {end}";
    }
}
