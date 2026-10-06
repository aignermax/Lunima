using CAP_Core.Routing;
using UnitTests.Import.Gds;
using UnitTests.Routing.ImportedGeometry;

namespace UnitTests.Services.GdsImport;

/// <summary>
/// A small synthetic chip drawn the way Nazca writes production layouts: every
/// straight and every arc of the top-cell route is its own ribbon polygon, the route
/// is drawn twice (2 µm core on (1,0), 6 µm cladding on (2,0)) with identical
/// centerlines, and the far device is a MIRRORED (STRANS) instance of an asymmetric
/// cell. Route: device A "out" (20, 5) → straight → R = 40 µm left bend → straight →
/// R = 40 µm right bend → straight → device B "in" (300, 145); GDS coordinates, µm.
/// </summary>
internal static class NazcaStyleChipFixture
{
    /// <summary>Core waveguide width (µm).</summary>
    public const double CoreWidthUm = 2.0;

    /// <summary>Cladding width (µm).</summary>
    public const double CladdingWidthUm = 6.0;

    /// <summary>Bend radius of both route bends (µm).</summary>
    public const double BendRadiusUm = 40.0;

    /// <summary>Centerline length of the drawn route (µm): straights 80 + 60 + 120 plus two quarter circles.</summary>
    public static double RouteLengthUm => 80 + 60 + 120 + 2 * (Math.PI / 2 * BendRadiusUm);

    /// <summary>The route pieces, in drawing order (GDS frame, Y up).</summary>
    private static IEnumerable<Func<double, List<(double X, double Y)>>> Pieces()
    {
        yield return w => RibbonTestPolygons.Straight(20, 5, 100, 5, w);
        yield return w => RibbonTestPolygons.Arc(new BendSegment(100, 45, BendRadiusUm, 0, 90), w);
        yield return w => RibbonTestPolygons.Straight(140, 45, 140, 105, w);
        yield return w => RibbonTestPolygons.Arc(new BendSegment(180, 105, BendRadiusUm, 90, -90), w);
        yield return w => RibbonTestPolygons.Straight(180, 145, 300, 145, w);
    }

    /// <summary>Writes the chip as a GDS II stream (1 nm database unit).</summary>
    public static byte[] Build()
    {
        var writer = GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("dev", 0, 0)
                .SRef("dev", 300_000, 150_000, reflected: true);
        foreach (var piece in Pieces())
        {
            writer.Boundary(1, 0, ToNm(piece(CoreWidthUm)));
            writer.Boundary(2, 0, ToNm(piece(CladdingWidthUm)));
        }
        return writer
            .EndCell()
            .BeginCell("dev")
                // 20 × 2 µm core stripe with port labels, plus an orientation marker in
                // the upper-left corner that makes the mirror visible.
                .Boundary(1, 0, (0, 4000), (20000, 4000), (20000, 6000), (0, 6000), (0, 4000))
                .Boundary(5, 0, (0, 6000), (4000, 6000), (0, 10000), (0, 6000))
                .Text(1, 10, "in", 0, 5000)
                .Text(1, 10, "out", 20000, 5000)
            .EndCell()
            .EndLibrary()
            .ToArray();
    }

    /// <summary>Writes the chip to <paramref name="directory"/> and returns the file path.</summary>
    public static string WriteTo(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "nazca-style-chip.gds");
        File.WriteAllBytes(path, Build());
        return path;
    }

    private static (int X, int Y)[] ToNm(List<(double X, double Y)> outline)
    {
        var points = outline.Select(p => ((int)Math.Round(p.X * 1000), (int)Math.Round(p.Y * 1000))).ToList();
        points.Add(points[0]);
        return points.ToArray();
    }
}
