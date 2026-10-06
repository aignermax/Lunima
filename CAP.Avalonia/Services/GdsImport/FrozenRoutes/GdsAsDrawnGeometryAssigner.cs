using CAP_Core.Components.Core;
using CAP_Core.Routing.ImportedGeometry;
using CAP_DataAccess.Import.Gds;

namespace CAP.Avalonia.Services.GdsImport.FrozenRoutes;

/// <summary>
/// Distributes an imported layout's top-cell polygons onto the routes they belong
/// to. Layout tools draw a waveguide on several layers at once — the core plus
/// cladding, trench or exclusion layers — each layer as its own ribbon with the
/// SAME centerline end points as the core piece. A polygon whose fitted centerline
/// starts and ends where one of an owner's core pieces does is part of that owner's
/// drawn geometry; everything else (logos, markers, pads) stays unassigned.
/// </summary>
public sealed class GdsAsDrawnGeometryAssigner
{
    /// <summary>Largest end-point mismatch (µm) between a layer piece and its core piece.</summary>
    public const double MatchToleranceUm = 0.05;

    private const double BucketSizeUm = 1.0;

    /// <summary>
    /// Largest bounding-box extent (µm) of a marker polygon (pin arrows, port marks
    /// that layout tools stamp at every piece end).
    /// </summary>
    public const double MarkerMaxExtentUm = 2.5;

    /// <summary>Most vertices a marker polygon has (pin arrows are 3–7-gons; vias and pads are not markers).</summary>
    public const int MarkerMaxVertices = 12;

    /// <summary>How far (µm) a piece end may sit outside a marker's bounding box and still own it.</summary>
    public const double MarkerReachUm = 1.0;

    private readonly Dictionary<(long, long), List<(int Owner, (double X, double Y) A, (double X, double Y) B)>> _buckets = new();
    private readonly Dictionary<(long, long), List<(int Owner, (double X, double Y) Point)>> _endpointBuckets = new();
    private readonly List<List<OutlinePolygon>> _ownerPolygons = new();

    /// <summary>
    /// Registers an owner (a route or a pin-less frozen path) with its core pieces and
    /// the core polygons they were fitted from (canvas coordinates).
    /// </summary>
    /// <param name="pieces">Fitted core pieces, canvas coordinates.</param>
    /// <param name="corePolygons">The owner's own polygons, canvas coordinates.</param>
    /// <returns>The owner index to read <see cref="PolygonsOf"/> with.</returns>
    public int AddOwner(IEnumerable<RibbonFit> pieces, IEnumerable<OutlinePolygon> corePolygons)
    {
        int owner = _ownerPolygons.Count;
        _ownerPolygons.Add(corePolygons.ToList());
        foreach (var piece in pieces)
        {
            Bucket(piece.Start).Add((owner, piece.Start, piece.End));
            AddEndpoint(owner, piece.Start);
            AddEndpoint(owner, piece.End);
        }
        return owner;
    }

    /// <summary>The owner's polygons: its core polygons plus every layer piece assigned to it.</summary>
    /// <param name="owner">Index returned by <see cref="AddOwner"/>.</param>
    public IReadOnlyList<OutlinePolygon> PolygonsOf(int owner) => _ownerPolygons[owner];

    /// <summary>
    /// Assigns each polygon to the owner whose core piece it shadows; returns the
    /// polygons no owner claimed.
    /// </summary>
    /// <param name="polygons">Candidate polygons, plan space.</param>
    /// <param name="offsetXUm">Plan-to-canvas X translation (µm).</param>
    /// <param name="offsetYUm">Plan-to-canvas Y translation (µm).</param>
    public List<OutlinePolygon> Assign(IEnumerable<GdsOutlinePolygon> polygons, double offsetXUm, double offsetYUm)
    {
        var unassigned = new List<OutlinePolygon>();
        foreach (var polygon in polygons)
        {
            var canvasPolygon = ToOutline(polygon, offsetXUm, offsetYUm);
            int owner = FindRibbonOwner(GdsCenterlineRouteBuilder.ToCanvas(polygon, offsetXUm, offsetYUm));
            if (owner < 0)
                owner = FindMarkerOwner(canvasPolygon);
            if (owner < 0)
                unassigned.Add(canvasPolygon);
            else
                _ownerPolygons[owner].Add(canvasPolygon);
        }
        return unassigned;
    }

    /// <summary>Converts a plan-space import polygon into a canvas-space outline polygon.</summary>
    public static OutlinePolygon ToOutline(GdsOutlinePolygon polygon, double offsetXUm, double offsetYUm) => new()
    {
        Layer = polygon.Layer,
        DataType = polygon.DataType,
        Points = polygon.Points.Select(p => new OutlinePoint(p.X + offsetXUm, p.Y + offsetYUm)).ToList(),
    };

    /// <summary>
    /// The owner whose core piece the polygon shadows. A rectangle shorter than it is
    /// wide (a short straight on a wide cladding layer) fits with its caps on the
    /// long edges, so rectangles are also tried along their other axis.
    /// </summary>
    private int FindRibbonOwner(List<(double X, double Y)> outline)
    {
        var fit = RibbonCenterlineFitter.Fit(outline);
        if (fit is null) return -1;
        int owner = FindOwner(fit.Start, fit.End);
        if (owner >= 0 || fit.Kind != RibbonFitKind.Straight) return owner;
        var corners = outline.Distinct().ToList();
        if (corners.Count != 4) return -1;
        var alternativeStart = Mid(corners[1], corners[2]);
        var alternativeEnd = Mid(corners[3], corners[0]);
        int alternative = FindOwner(alternativeStart, alternativeEnd);
        return alternative >= 0 ? alternative : FindOwner(Mid(corners[0], corners[1]), Mid(corners[2], corners[3]));
    }

    /// <summary>
    /// The owner of a small marker polygon: the owner with a core piece end inside the
    /// marker's bounding box (grown by <see cref="MarkerReachUm"/>).
    /// </summary>
    private int FindMarkerOwner(OutlinePolygon polygon)
    {
        if (polygon.Points.Count > MarkerMaxVertices + 1) return -1;
        double minX = polygon.Points.Min(p => p.X), maxX = polygon.Points.Max(p => p.X);
        double minY = polygon.Points.Min(p => p.Y), maxY = polygon.Points.Max(p => p.Y);
        if (maxX - minX > MarkerMaxExtentUm || maxY - minY > MarkerMaxExtentUm) return -1;
        var (bx, by) = Key(((minX + maxX) / 2, (minY + maxY) / 2));
        int reach = (int)Math.Ceiling((MarkerMaxExtentUm / 2 + MarkerReachUm) / BucketSizeUm);
        for (long dx = -reach; dx <= reach; dx++)
        for (long dy = -reach; dy <= reach; dy++)
        {
            if (!_endpointBuckets.TryGetValue((bx + dx, by + dy), out var entries)) continue;
            foreach (var (owner, point) in entries)
            {
                if (point.X >= minX - MarkerReachUm && point.X <= maxX + MarkerReachUm
                    && point.Y >= minY - MarkerReachUm && point.Y <= maxY + MarkerReachUm)
                    return owner;
            }
        }
        return -1;
    }

    private static (double X, double Y) Mid((double X, double Y) a, (double X, double Y) b) =>
        ((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private int FindOwner((double X, double Y) start, (double X, double Y) end) =>
        FindDirected(start, end) is var forward and >= 0 ? forward : FindDirected(end, start);

    private int FindDirected((double X, double Y) a, (double X, double Y) b)
    {
        var (bx, by) = Key(a);
        for (long dx = -1; dx <= 1; dx++)
        for (long dy = -1; dy <= 1; dy++)
        {
            if (!_buckets.TryGetValue((bx + dx, by + dy), out var entries)) continue;
            foreach (var entry in entries)
            {
                if (Close(entry.A, a) && Close(entry.B, b))
                    return entry.Owner;
            }
        }
        return -1;
    }

    private List<(int, (double, double), (double, double))> Bucket((double X, double Y) point)
    {
        var key = Key(point);
        if (!_buckets.TryGetValue(key, out var list))
            _buckets[key] = list = new();
        return list;
    }

    private void AddEndpoint(int owner, (double X, double Y) point)
    {
        var key = Key(point);
        if (!_endpointBuckets.TryGetValue(key, out var list))
            _endpointBuckets[key] = list = new();
        list.Add((owner, point));
    }

    private static (long, long) Key((double X, double Y) p) =>
        ((long)Math.Floor(p.X / BucketSizeUm), (long)Math.Floor(p.Y / BucketSizeUm));

    private static bool Close((double X, double Y) p, (double X, double Y) q) =>
        Math.Abs(p.X - q.X) <= MatchToleranceUm && Math.Abs(p.Y - q.Y) <= MatchToleranceUm;
}
