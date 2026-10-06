using CAP_Core.Routing;
using UnitTests.Import.Gds;
using UnitTests.Routing.ImportedGeometry;
using static UnitTests.Services.GdsImport.ProductionScale.ProductionScaleCells;

namespace UnitTests.Services.GdsImport.ProductionScale;

/// <summary>
/// A synthetic 16.6 × 13.1 mm chip with the STRUCTURE of a production InP layout (and
/// none of its design): 159 instances — a die frame, 16 composite blocks holding 85 device
/// references each, 136 edge couplers in two columns (one column mirrored), logos and
/// pads — and 128 fan-out routes drawn Nazca-style, every straight and arc its own ribbon
/// on four layers (core 401, 404, and the wide, finely discretized 12/18 claddings), with
/// pin markers on (234,0) at every piece end. Polygon and vertex counts per layer match
/// the production file to within a few percent, so import, render and routing timings
/// measured here transfer. Deterministic.
/// </summary>
internal static class ProductionScaleChipFixture
{
    public const double ChipWidthUm = 16600, ChipHeightUm = 13100;
    public const int CompositeCount = 16, CouplersPerSide = 68;
    public const double CompositeX = 3200, CompositeWidth = 10187, CompositePitch = 750;
    public const double BendRadiusUm = 100, LanePitchUm = 20, CouplerDrop = 300;

    /// <summary>Routes: every composite port (4 per side per block) to its own edge coupler.</summary>
    public const int RouteCount = 2 * CompositeCount * CompositePortCount;

    private static readonly (string Name, double W, double H, (int, int, int)[] Layers)[] DeviceTypes =
    {
        ("dev_a", 100, 60, new[] { (55, 1, 4), (59, 4, 4), (232, 12, 6), (234, 4, 7), (237, 3, 4), (402, 2, 4), (450, 3, 19), (459, 8, 13) }),
        ("dev_b", 205, 40, new[] { (55, 1, 4), (59, 2, 4), (232, 12, 6), (234, 2, 7), (402, 2, 4), (404, 2, 4), (459, 20, 12) }),
        ("dev_c", 246, 50, new[] { (55, 1, 4), (59, 3, 4), (232, 12, 6), (234, 3, 7), (404, 4, 7), (459, 17, 12) }),
        ("dev_d", 750, 120, new[] { (55, 1, 4), (59, 4, 4), (232, 12, 6), (234, 4, 7), (237, 3, 4), (250, 1, 4), (404, 2, 4), (450, 3, 4), (459, 12, 14) }),
        ("dev_e", 1581, 100, new[] { (55, 1, 4), (59, 8, 4), (232, 12, 6), (234, 8, 7), (404, 26, 11), (450, 6, 4), (459, 14, 12) }),
        ("dev_f", 910, 70, new[] { (55, 1, 4), (59, 4, 4), (232, 12, 6), (234, 4, 7), (404, 3, 4), (450, 2, 4), (459, 21, 12) }),
        ("pad_s", 108, 108, new[] { (51, 1, 200), (52, 1, 200), (54, 1, 200), (450, 1, 200), (451, 1, 200) }),
    };

    /// <summary>Writes the chip to <paramref name="directory"/> and returns the file path.</summary>
    public static string WriteTo(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "production-scale-chip.gds");
        File.WriteAllBytes(path, Build());
        return path;
    }

    /// <summary>Builds the GDS II stream.</summary>
    public static byte[] Build()
    {
        var w = GdsTestWriter.Create().StandardPrologue();
        w.BeginCell("TOP").SRef("frame", 0, 0);
        for (int k = 0; k < CompositeCount; k++)
            w.SRef("block", Nm(CompositeX), Nm(CompositeY(k)));
        for (int i = 0; i < CouplersPerSide; i++)
        {
            double yc = CouplerPortY(i);
            w.SRef("coupler", Nm(100), Nm(yc + 57), reflected: true);
            w.SRef("coupler", Nm(16500), Nm(yc + 57), angleDegrees: 180);
        }
        w.SRef("logo_a", Nm(3200), Nm(12550)).SRef("logo_b", Nm(9000), Nm(12550));
        w.SRef("pad_l", Nm(14000), Nm(100)).SRef("pad_l", Nm(14300), Nm(100))
         .SRef("pad_s", Nm(14600), Nm(100)).SRef("pad_s", Nm(14800), Nm(100));
        for (int i = 0; i < CompositeCount * CompositePortCount; i++)
        {
            WriteRoute(w, LeftRoute(i));
            WriteRoute(w, RightRoute(i));
        }
        w.EndCell();

        Frame(w, "frame", ChipWidthUm, ChipHeightUm);
        Composite(w, "block", DeviceTypes.Take(6).Select(d => d.Name).ToList(), childCount: 85, seed: 7);
        foreach (var (name, width, height, layers) in DeviceTypes)
            Device(w, name, width, height, layers.Select(l => (l.Item1, l.Item2, l.Item3)), name.GetHashCode() & 0xffff);
        EdgeCoupler(w, "coupler", seed: 11);
        Logo(w, "logo_a", seed: 3);
        Logo(w, "logo_b", seed: 4);
        Pad(w, "pad_l");
        return w.EndLibrary().ToArray();
    }

    private static double CompositeY(int k) => 300 + k * CompositePitch;

    /// <summary>Y of route <paramref name="i"/>'s composite port (ports sorted bottom to top).</summary>
    private static double PortY(int i) => CompositeY(i / CompositePortCount) + CompositePortY(i % CompositePortCount);

    /// <summary>Y of coupler <paramref name="i"/>'s port: routed couplers sit <see cref="CouplerDrop"/> below their port.</summary>
    private static double CouplerPortY(int i) =>
        i < CompositeCount * CompositePortCount ? PortY(i) - CouplerDrop : PortY(CompositeCount * CompositePortCount - 1) + 200 + (i - 64) * 130;

    /// <summary>Left fan-out: coupler port east, up a lane, east into the block. Lanes move left as i grows (crossing-free).</summary>
    private static IEnumerable<Func<double, List<(double X, double Y)>>> LeftRoute(int i)
    {
        double yc = CouplerPortY(i), yp = PortY(i), lane = 2900 - i * LanePitchUm, r = BendRadiusUm;
        return Split(1300, yc, lane - r, yc, 2)
            .Append(wd => RibbonTestPolygons.Arc(new BendSegment(lane - r, yc + r, r, 0, 90), wd, Samples(wd)))
            .Concat(Split(lane, yc + r, lane, yp - r, 3))
            .Append(wd => RibbonTestPolygons.Arc(new BendSegment(lane + r, yp - r, r, 90, -90), wd, Samples(wd)))
            .Concat(Split(lane + r, yp, CompositeX, yp, 2));
    }

    /// <summary>Right fan-out, mirrored: block port east, down a lane, east into the coupler. Lanes move right as i grows.</summary>
    private static IEnumerable<Func<double, List<(double X, double Y)>>> RightRoute(int i)
    {
        double yc = CouplerPortY(i), yp = PortY(i), lane = 13687 + i * LanePitchUm, r = BendRadiusUm;
        double start = CompositeX + CompositeWidth;
        return Split(start, yp, lane - r, yp, 2)
            .Append(wd => RibbonTestPolygons.Arc(new BendSegment(lane - r, yp - r, r, 0, -90), wd, Samples(wd)))
            .Concat(Split(lane, yp - r, lane, yc + r, 3))
            .Append(wd => RibbonTestPolygons.Arc(new BendSegment(lane + r, yc + r, r, 270, 90), wd, Samples(wd)))
            .Concat(Split(lane + r, yc, 15300, yc, 2));
    }

    /// <summary>A straight run drawn as <paramref name="pieces"/> abutting ribbons, as layout tools emit long straights.</summary>
    private static IEnumerable<Func<double, List<(double X, double Y)>>> Split(double x0, double y0, double x1, double y1, int pieces)
    {
        for (int k = 0; k < pieces; k++)
        {
            double t0 = (double)k / pieces, t1 = (double)(k + 1) / pieces;
            double ax = x0 + (x1 - x0) * t0, ay = y0 + (y1 - y0) * t0, bx = x0 + (x1 - x0) * t1, by = y0 + (y1 - y0) * t1;
            yield return wd => RibbonTestPolygons.Straight(ax, ay, bx, by, wd);
        }
    }

    /// <summary>
    /// Points per arc side, tuned so the vertex totals match the production file: its
    /// wide cladding arcs carry ~10× the vertices of the core arcs (~300 k per cladding layer).
    /// </summary>
    private static int Samples(double width) => width > 10 ? 560 : 62;

    private static void WriteRoute(GdsTestWriter w, IEnumerable<Func<double, List<(double X, double Y)>>> pieces)
    {
        foreach (var piece in pieces)
        {
            var core = piece(2);
            w.Boundary(401, 0, Nm(core))
             .Boundary(404, 0, Nm(piece(6)))
             .Boundary(12, 0, Nm(piece(16)))
             .Boundary(18, 0, Nm(piece(18)));
            WriteMarker(w, core[0], core[^1]);
            WriteMarker(w, core[core.Count / 2 - 1], core[core.Count / 2]);
        }
    }

    /// <summary>A 7-vertex pin arrow centred between two outline points (a piece cap).</summary>
    private static void WriteMarker(GdsTestWriter w, (double X, double Y) a, (double X, double Y) b)
    {
        double cx = (a.X + b.X) / 2, cy = (a.Y + b.Y) / 2;
        w.Boundary(234, 0, Nm(new List<(double X, double Y)>
        {
            (cx - 0.5, cy - 0.35), (cx, cy - 0.35), (cx, cy - 0.5), (cx + 0.5, cy),
            (cx, cy + 0.5), (cx, cy + 0.35), (cx - 0.5, cy + 0.35),
        }));
    }
}
