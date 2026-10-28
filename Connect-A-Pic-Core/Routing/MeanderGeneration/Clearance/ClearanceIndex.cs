namespace CAP_Core.Routing.MeanderGeneration.Clearance;

/// <summary>
/// Uniform-grid spatial index over the sampled centerlines of routes (keyed by an owner, so a
/// route can be replaced in place) and over component footprints. Lets the meander search ask
/// "what is near this piece of geometry" in time proportional to the neighbourhood instead of
/// the whole design — the difference between seconds and minutes when many routes are matched
/// in one go. Not thread-safe: one search uses one index.
/// </summary>
public sealed class ClearanceIndex
{
    /// <summary>Default grid cell edge (µm): a few waveguide pitches.</summary>
    public const double DefaultCellSizeMicrometers = 40.0;
    private const int MaxCellsPerAxis = 1 << 20;

    private readonly double _cellSize;
    private readonly Dictionary<long, List<int>> _cells = new();
    private readonly List<IndexedSegment> _segments = new();
    private readonly List<MeanderBounds> _footprints = new();
    private readonly List<object?> _footprintOwners = new();
    private readonly Dictionary<object, List<int>> _segmentsByOwner = new(ReferenceEqualityComparer.Instance);
    private int[] _stamps = Array.Empty<int>();
    private int[] _footprintStamps = Array.Empty<int>();
    private int _query;
    private int _liveSegments;

    /// <summary>One sampled chord of a route centerline.</summary>
    internal readonly record struct IndexedSegment(double Ax, double Ay, double Bx, double By, object Owner, bool Alive);

    /// <summary>Creates an empty index.</summary>
    public ClearanceIndex(double cellSizeMicrometers = DefaultCellSizeMicrometers)
    {
        if (!(cellSizeMicrometers > 0))
            throw new ArgumentOutOfRangeException(nameof(cellSizeMicrometers));
        _cellSize = cellSizeMicrometers;
    }

    /// <summary>True when the index holds no live route and no footprint.</summary>
    public bool IsEmpty => _liveSegments == 0 && _footprints.Count == 0;

    /// <summary>Indexes <paramref name="path"/> under <paramref name="owner"/>, replacing any route it had.</summary>
    public void SetRoute(object owner, RoutedPath path)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(path);
        RemoveRoute(owner);
        var ids = new List<int>();
        var points = ClearanceGeometry.Sample(path);
        for (int i = 0; i < points.Count - 1; i++)
        {
            int id = _segments.Count;
            var (a, b) = (points[i], points[i + 1]);
            _segments.Add(new IndexedSegment(a.X, a.Y, b.X, b.Y, owner, true));
            ids.Add(id);
            Insert(id, Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }
        _liveSegments += ids.Count;
        _segmentsByOwner[owner] = ids;
    }

    /// <summary>Drops the route indexed under <paramref name="owner"/>, if any.</summary>
    public void RemoveRoute(object owner)
    {
        if (!_segmentsByOwner.Remove(owner, out var ids))
            return;
        foreach (int id in ids)
            _segments[id] = _segments[id] with { Alive = false };
        _liveSegments -= ids.Count;
    }

    /// <summary>Indexes a component footprint the meander may not enter.</summary>
    /// <param name="footprint">The component's rectangle.</param>
    /// <param name="owner">The component, so a route's own endpoint components can be ignored.</param>
    public void AddFootprint(MeanderBounds footprint, object? owner = null)
    {
        int id = _footprints.Count;
        _footprints.Add(footprint);
        _footprintOwners.Add(owner);
        Insert(~id, footprint.MinX, footprint.MinY, footprint.MaxX, footprint.MaxY);
    }

    /// <summary>
    /// Collects the live route chords and the footprints not owned by <paramref name="ignoredOwners"/>
    /// whose grid cells overlap the box; each item is reported once per call.
    /// </summary>
    internal void Query(double minX, double minY, double maxX, double maxY, IReadOnlySet<object>? ignoredOwners,
        List<IndexedSegment> segments, List<MeanderBounds> footprints)
    {
        segments.Clear();
        footprints.Clear();
        BeginQuery();
        var (x0, y0) = Cell(minX, minY);
        var (x1, y1) = Cell(maxX, maxY);
        for (long cx = x0; cx <= x1; cx++)
        for (long cy = y0; cy <= y1; cy++)
        {
            if (!_cells.TryGetValue(Key(cx, cy), out var ids))
                continue;
            foreach (int id in ids)
                Report(id, ignoredOwners, segments, footprints);
        }
    }

    private void Report(int id, IReadOnlySet<object>? ignoredOwners, List<IndexedSegment> segments,
        List<MeanderBounds> footprints)
    {
        if (id < 0)
        {
            int footprint = ~id;
            if (_footprintStamps[footprint] == _query)
                return;
            _footprintStamps[footprint] = _query;
            if (_footprintOwners[footprint] is not { } owner || ignoredOwners == null || !ignoredOwners.Contains(owner))
                footprints.Add(_footprints[footprint]);
            return;
        }
        if (_stamps[id] == _query)
            return;
        _stamps[id] = _query;
        var segment = _segments[id];
        if (segment.Alive && (ignoredOwners == null || !ignoredOwners.Contains(segment.Owner)))
            segments.Add(segment);
    }

    private void BeginQuery()
    {
        if (_stamps.Length < _segments.Count)
            Array.Resize(ref _stamps, Math.Max(_segments.Count, _stamps.Length * 2));
        if (_footprintStamps.Length < _footprints.Count)
            Array.Resize(ref _footprintStamps, Math.Max(_footprints.Count, _footprintStamps.Length * 2));
        _query++;
    }

    private void Insert(int id, double minX, double minY, double maxX, double maxY)
    {
        var (x0, y0) = Cell(minX, minY);
        var (x1, y1) = Cell(maxX, maxY);
        for (long cx = x0; cx <= x1; cx++)
        for (long cy = y0; cy <= y1; cy++)
        {
            long key = Key(cx, cy);
            if (!_cells.TryGetValue(key, out var ids))
                _cells[key] = ids = new List<int>();
            ids.Add(id);
        }
    }

    private (long X, long Y) Cell(double x, double y) => (
        Math.Clamp((long)Math.Floor(x / _cellSize), -MaxCellsPerAxis, MaxCellsPerAxis),
        Math.Clamp((long)Math.Floor(y / _cellSize), -MaxCellsPerAxis, MaxCellsPerAxis));

    private static long Key(long x, long y) => (x << 32) ^ (y & 0xFFFFFFFFL);
}
