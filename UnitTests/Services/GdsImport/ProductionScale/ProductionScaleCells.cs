using UnitTests.Import.Gds;

namespace UnitTests.Services.GdsImport.ProductionScale;

/// <summary>
/// Cell builders for <see cref="ProductionScaleChipFixture"/>: devices, edge couplers, a
/// composite block, logos, pads and a die frame. Shapes are synthetic; what matches a
/// production InP chip is the structure — the layers, the polygon and vertex counts per
/// layer, port labels on (235,0), waveguide stubs on (401,0) reaching the cell edges.
/// </summary>
internal static class ProductionScaleCells
{
    /// <summary>Port label layer.</summary>
    public const int PortLayer = 235;

    /// <summary>Waveguide core layer.</summary>
    public const int CoreLayer = 401;

    /// <summary>
    /// A device cell of <paramref name="widthUm"/> × <paramref name="heightUm"/> with ports
    /// "in" (left edge) and "out" (right edge) at mid height, a core stripe between them,
    /// and per (layer, count, vertices) the requested polygons tiled across the cell.
    /// </summary>
    public static void Device(GdsTestWriter w, string name, double widthUm, double heightUm,
        IEnumerable<(int Layer, int Count, int Vertices)> layers, int seed)
    {
        var random = new Random(seed);
        double mid = heightUm / 2;
        w.BeginCell(name)
            .Boundary(CoreLayer, 0, Nm(Rect(0, mid - 1, widthUm, mid + 1)))
            .Text(PortLayer, 0, "in", Nm(0), Nm(mid))
            .Text(PortLayer, 0, "out", Nm(widthUm), Nm(mid));
        Annotate(w, widthUm, heightUm);
        foreach (var (layer, count, vertices) in layers)
            Scatter(w, layer, count, vertices, widthUm, heightUm, random);
        w.EndCell();
    }

    /// <summary>
    /// An edge coupler: a long taper body with one port "o1" at its inner (right) end and
    /// an asymmetric marker so a mirrored instance is visibly mirrored.
    /// </summary>
    public static void EdgeCoupler(GdsTestWriter w, string name, int seed)
    {
        const double length = 1200, height = 114, mid = 57;
        var random = new Random(seed);
        w.BeginCell(name)
            .Boundary(CoreLayer, 0, Nm(Rect(0, mid - 1, length, mid + 1)))
            .Boundary(404, 0, Nm(Rect(0, mid - 3, length, mid + 3)))
            .Boundary(237, 0, Nm(Polygon(40, length * 0.5, height * 0.75, 20, 3)))
            .Text(PortLayer, 0, "o1", Nm(length), Nm(mid));
        Annotate(w, length, height);
        Scatter(w, 459, 30, 12, length, height, random);
        Scatter(w, 232, 12, 6, length, height, random);
        Scatter(w, 234, 2, 7, length, height, random);
        w.EndCell();
    }

    /// <summary>
    /// A composite block (10 187 × 969 µm) with ports a0..a3 (left) and b0..b3 (right),
    /// ~1 060 own polygons on the production layer mix and <paramref name="childCount"/>
    /// device references in rows.
    /// </summary>
    public static void Composite(GdsTestWriter w, string name, IReadOnlyList<string> devices, int childCount, int seed)
    {
        const double width = 10187, height = 969;
        var random = new Random(seed);
        w.BeginCell(name);
        for (int p = 0; p < CompositePortCount; p++)
        {
            double y = CompositePortY(p);
            w.Boundary(CoreLayer, 0, Nm(Rect(0, y - 1, 60, y + 1)))
             .Boundary(CoreLayer, 0, Nm(Rect(width - 60, y - 1, width, y + 1)))
             .Text(PortLayer, 0, $"a{p}", 0, Nm(y))
             .Text(PortLayer, 0, $"b{p}", Nm(width), Nm(y));
        }
        foreach (var (layer, count, vertices) in new[]
        {
            (12, 73, 183), (18, 73, 183), (51, 126, 63), (52, 126, 64), (54, 126, 65),
            (234, 136, 7), (401, 73, 24), (404, 73, 24), (450, 126, 11), (451, 126, 11),
        })
            Scatter(w, layer, count, vertices, width, height, random);
        for (int i = 0; i < childCount; i++)
        {
            var device = devices[i % devices.Count];
            w.SRef(device, Nm(200 + (i % 17) * 500), Nm(120 + (i / 17) * 170), angleDegrees: i % 7 == 0 ? 180 : null);
        }
        w.EndCell();
    }

    /// <summary>
    /// The Nazca PDK version stamp production black-box cells carry: it marks the file as
    /// foreign, so the import dialog clears its own layer defaults as for a real foundry file.
    /// </summary>
    private static void Annotate(GdsTestWriter w, double widthUm, double heightUm) =>
        w.Text(56, 0, "nazca_pdk_version: synthetic-1.0", Nm(widthUm / 2), Nm(heightUm / 2));

    /// <summary>Number of ports per side of a composite block.</summary>
    public const int CompositePortCount = 4;

    /// <summary>Y of composite port <paramref name="index"/> inside the block (µm).</summary>
    public static double CompositePortY(int index) => 150 + index * 200;

    /// <summary>A logo cell: 11 dense blob polygons on (24,0), ~9 800 vertices in total.</summary>
    public static void Logo(GdsTestWriter w, string name, int seed)
    {
        var random = new Random(seed);
        w.BeginCell(name);
        for (int i = 0; i < 11; i++)
            w.Boundary(24, 0, Nm(Polygon(890, 150 + i * 300, 250, 120 + random.NextDouble() * 20, 3 + i % 5)));
        w.EndCell();
    }

    /// <summary>A metal pad stack (five 200-vertex rings).</summary>
    public static void Pad(GdsTestWriter w, string name)
    {
        w.BeginCell(name);
        foreach (var layer in new[] { 51, 52, 54, 450, 451 })
            w.Boundary(layer, 0, Nm(Polygon(200, 54, 54, 50, 0)));
        w.EndCell();
    }

    /// <summary>
    /// The die frame: a chip-sized outline and a waveguide stub at its edge (guessed pins);
    /// it encloses every other cell and must import as background.
    /// </summary>
    public static void Frame(GdsTestWriter w, string name, double widthUm, double heightUm)
    {
        w.BeginCell(name)
            .Boundary(7, 0, Nm(Rect(0, 0, widthUm, heightUm)))
            .Boundary(CoreLayer, 0, Nm(Rect(0, heightUm / 2 - 1, 80, heightUm / 2 + 1)))
            .Boundary(404, 0, Nm(Rect(0, heightUm / 2 - 3, 80, heightUm / 2 + 3)))
            .EndCell();
    }

    /// <summary>Polygons tiled over the cell: <paramref name="count"/> regular polygons of <paramref name="vertices"/> vertices.</summary>
    private static void Scatter(GdsTestWriter w, int layer, int count, int vertices, double width, double height, Random random)
    {
        int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count * width / height)));
        int rows = (int)Math.Ceiling(count / (double)columns);
        double cellW = width / columns, cellH = height / Math.Max(1, rows);
        for (int i = 0; i < count; i++)
        {
            double cx = (i % columns + 0.5) * cellW, cy = (i / columns + 0.5) * cellH;
            double r = Math.Max(0.5, Math.Min(cellW, cellH) * (0.2 + 0.2 * random.NextDouble()));
            w.Boundary(layer, 0, Nm(Polygon(Math.Max(3, vertices), cx, cy, r, random.Next(7))));
        }
    }

    /// <summary>Closed rectangle ring (µm).</summary>
    public static List<(double X, double Y)> Rect(double x0, double y0, double x1, double y1) =>
        new() { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };

    /// <summary>A star-ish closed ring of <paramref name="vertices"/> points (µm).</summary>
    private static List<(double X, double Y)> Polygon(int vertices, double cx, double cy, double radius, int lobes)
    {
        var ring = new List<(double X, double Y)>(vertices);
        for (int k = 0; k < vertices; k++)
        {
            double phi = 2 * Math.PI * k / vertices;
            double r = radius * (1 + (lobes > 0 ? 0.15 * Math.Sin(lobes * phi) : 0));
            ring.Add((cx + r * Math.Cos(phi), cy + r * Math.Sin(phi)));
        }
        return ring;
    }

    /// <summary>Converts a µm ring to a closed GDS nanometre ring.</summary>
    public static (int X, int Y)[] Nm(List<(double X, double Y)> ring)
    {
        var points = ring.Select(p => (Nm(p.X), Nm(p.Y))).ToList();
        points.Add(points[0]);
        return points.ToArray();
    }

    /// <summary>µm → nm.</summary>
    public static int Nm(double um) => (int)Math.Round(um * 1000);
}
