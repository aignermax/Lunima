using CAP_Core.Routing;

namespace CAP_Core.Analysis;

/// <summary>
/// Finds where two waveguide paths cross each other, so the keep-out rule of
/// <see cref="WaveguideSpacingDetector"/> stays silent around a crossing: two crossing
/// wires necessarily merge their keep-out bands there (the crossing itself is reported
/// by <see cref="ConnectionCrossingDetector"/>). Crossing points are computed lazily per
/// path pair, only for pairs that would otherwise be reported.
/// </summary>
internal sealed class WaveguideKeepOutCrossingExemption
{
    /// <summary>Bend sampling step used for the crossing test (fine, crossings are exact).</summary>
    private const double CrossingSampleSpacingMicrometers = 0.5;

    private readonly Dictionary<Guid, List<PathSegment>> _segmentsByPath = new();
    private readonly Dictionary<(Guid, Guid), List<(double X, double Y)>> _crossingsByPair = new();

    /// <summary>Indexes every segment by the path it belongs to.</summary>
    public WaveguideKeepOutCrossingExemption(IEnumerable<(Guid PathId, PathSegment Segment)> segments)
    {
        foreach (var (pathId, segment) in segments)
        {
            if (!_segmentsByPath.TryGetValue(pathId, out var list))
            {
                list = new List<PathSegment>();
                _segmentsByPath[pathId] = list;
            }
            list.Add(segment);
        }
    }

    /// <summary>
    /// True when the paths <paramref name="pathA"/> and <paramref name="pathB"/> cross each
    /// other within <paramref name="radiusMicrometers"/> of <paramref name="point"/>.
    /// </summary>
    public bool IsNearCrossing(Guid pathA, Guid pathB, (double X, double Y) point, double radiusMicrometers)
    {
        foreach (var (x, y) in CrossingsOf(pathA, pathB))
        {
            double dx = x - point.X;
            double dy = y - point.Y;
            if (dx * dx + dy * dy <= radiusMicrometers * radiusMicrometers)
                return true;
        }
        return false;
    }

    private List<(double X, double Y)> CrossingsOf(Guid pathA, Guid pathB)
    {
        var key = pathA.CompareTo(pathB) <= 0 ? (pathA, pathB) : (pathB, pathA);
        if (_crossingsByPair.TryGetValue(key, out var cached))
            return cached;

        var crossings = new List<(double X, double Y)>();
        if (_segmentsByPath.TryGetValue(pathA, out var segmentsA)
            && _segmentsByPath.TryGetValue(pathB, out var segmentsB))
        {
            foreach (var a in segmentsA)
            {
                foreach (var b in segmentsB)
                {
                    var (distance, at) = WaveguideSpacingGeometry.ComputeCenterlineDistance(
                        a, b, CrossingSampleSpacingMicrometers);
                    if (distance <= WaveguideSpacingGeometry.DistanceToleranceMicrometers)
                        crossings.Add(at);
                }
            }
        }

        _crossingsByPair[key] = crossings;
        return crossings;
    }
}
