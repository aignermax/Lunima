namespace CAP_Core.Routing;

/// <summary>
/// A routed path sampled once into the polyline <see cref="PathIntersectionDetector"/>
/// tests, with bounding boxes for the whole line and for runs of
/// <see cref="ChunkEdges"/> edges. Pairwise checks over a design compare every path
/// with every other; sampling each pair afresh and testing every edge against every
/// edge made a 130-route chip take tens of seconds. Boxes only prune edge pairs that
/// cannot intersect, so every verdict equals the unpruned one.
/// </summary>
public sealed class SampledPolyline
{
    /// <summary>Edges per bounding-box chunk.</summary>
    private const int ChunkEdges = 16;

    private readonly List<(double X, double Y)> _points;
    private readonly Box[] _chunks;

    private SampledPolyline(List<(double X, double Y)> points)
    {
        _points = points;
        int edges = Math.Max(0, points.Count - 1);
        _chunks = new Box[(edges + ChunkEdges - 1) / ChunkEdges];
        for (int c = 0; c < _chunks.Length; c++)
        {
            int first = c * ChunkEdges;
            int last = Math.Min(points.Count - 1, first + ChunkEdges);
            _chunks[c] = Box.Of(points, first, last);
        }
        Bounds = points.Count == 0 ? Box.Empty : Box.Of(points, 0, points.Count - 1);
    }

    /// <summary>Bounding box of the whole polyline.</summary>
    internal Box Bounds { get; }

    /// <summary>Samples <paramref name="path"/> the same way <see cref="PathIntersectionDetector.Crosses"/> does.</summary>
    /// <param name="path">The routed path to sample.</param>
    public static SampledPolyline From(RoutedPath path) => new(PathIntersectionDetector.SamplePolyline(path));

    /// <summary>
    /// True when the two polylines properly cross (touching joints do not count) —
    /// the verdict of <see cref="PathIntersectionDetector.Crosses"/>.
    /// </summary>
    /// <param name="other">The other sampled path.</param>
    public bool Crosses(SampledPolyline other)
    {
        if (_points.Count < 2 || other._points.Count < 2 || !Bounds.Overlaps(other.Bounds))
            return false;

        for (int ca = 0; ca < _chunks.Length; ca++)
        {
            if (!_chunks[ca].Overlaps(other.Bounds)) continue;
            for (int cb = 0; cb < other._chunks.Length; cb++)
            {
                if (_chunks[ca].Overlaps(other._chunks[cb]) && ChunksIntersect(ca, other, cb))
                    return true;
            }
        }
        return false;
    }

    private bool ChunksIntersect(int chunkA, SampledPolyline other, int chunkB)
    {
        int aEnd = Math.Min(_points.Count - 1, (chunkA + 1) * ChunkEdges);
        int bEnd = Math.Min(other._points.Count - 1, (chunkB + 1) * ChunkEdges);
        for (int i = chunkA * ChunkEdges; i < aEnd; i++)
        {
            for (int j = chunkB * ChunkEdges; j < bEnd; j++)
            {
                if (PathIntersectionDetector.SegmentsIntersect(_points[i], _points[i + 1], other._points[j], other._points[j + 1]))
                    return true;
            }
        }
        return false;
    }

    /// <summary>An axis-aligned bounding box.</summary>
    internal readonly record struct Box(double MinX, double MinY, double MaxX, double MaxY)
    {
        public static Box Empty => new(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);

        /// <summary>True when the boxes overlap or touch.</summary>
        public bool Overlaps(Box other) =>
            MinX <= other.MaxX && other.MinX <= MaxX && MinY <= other.MaxY && other.MinY <= MaxY;

        /// <summary>Box over the points with indices <paramref name="first"/>..<paramref name="last"/> (inclusive).</summary>
        public static Box Of(List<(double X, double Y)> points, int first, int last)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = first; i <= last; i++)
            {
                var (x, y) = points[i];
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            return new Box(minX, minY, maxX, maxY);
        }
    }
}
