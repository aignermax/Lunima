using CAP.Avalonia.Services;
using CAP_Core.Export;
using CAP_DataAccess.Import.Gds;
using Shouldly;
using Xunit;

namespace UnitTests.Export.WaveguideCompanions;

/// <summary>
/// End to end through real Nazca: the exported script renders a GDS whose trench layer
/// carries one band polygon per routed segment (two straights + one arc), each 2 × 8 µm
/// wider than the core it follows. Skips silently when Python/Nazca are not installed
/// (the string-level contract is covered by <see cref="SimpleNazcaExporterCompanionLayerTests"/>).
/// </summary>
[Trait("Category", "Slow")]
public class CompanionLayerGdsExportTests
{
    private const double Tolerance = 0.01;

    [Fact]
    public async Task ExportedGds_ContainsTrenchBandAlongStraightAndArcSegments()
    {
        var service = new GdsExportService();
        var environment = await service.CheckPythonEnvironmentAsync();
        if (!environment.PythonAvailable || !environment.NazcaAvailable)
            return;

        var (canvas, _) = CompanionLayerTestCanvas.Create(CompanionLayerTestCanvas.TrenchSpec());
        var directory = Path.Combine(Path.GetTempPath(), $"cap_companions_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var scriptPath = Path.Combine(directory, "companions.py");
            await File.WriteAllTextAsync(scriptPath, new SimpleNazcaExporter().Export(canvas));

            var result = await service.ExportToGdsAsync(scriptPath, generateGds: true);
            result.Success.ShouldBeTrue(result.Status);

            await using var stream = File.OpenRead(result.GdsPath!);
            var library = await new GdsReader().ReadAsync(stream);
            var polygons = library.Cells.Values.SelectMany(c => c.Elements).OfType<GdsPolygon>().ToList();
            // The component stubs are demofab cells that draw their own trench on 2/0 too;
            // only polygons inside the route's extent belong to the routed waveguide.
            var bands = polygons
                .Where(p => p.Layer == CompanionLayerTestCanvas.TrenchLayer && p.DataType == 0)
                .Where(p => p.Points.All(q => q.X >= -1 && q.X <= 130 && q.Y >= -130 && q.Y <= 10))
                .ToList();

            bands.Count.ShouldBe(3, "one band per straight and arc segment");
            AssertFirstStraightBand(bands);
            AssertArcBandReachesBeyondOuterRadius(bands);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertFirstStraightBand(List<GdsPolygon> bands)
    {
        // The first straight runs along y = 0 from x = 0 to 100; its band is the core grown
        // by 8 µm on each side (core width is whatever the core call draws, 0.45–1 µm).
        var band = bands.Single(b => b.Points.All(p => p.X >= -Tolerance && p.X <= 100 + Tolerance)
                                     && b.Points.Max(p => p.X) > 99);
        double height = band.Points.Max(p => p.Y) - band.Points.Min(p => p.Y);
        height.ShouldBeInRange(2 * CompanionLayerTestCanvas.TrenchOffsetMicrometers + 0.4,
            2 * CompanionLayerTestCanvas.TrenchOffsetMicrometers + 1.0 + Tolerance);
    }

    private static void AssertArcBandReachesBeyondOuterRadius(List<GdsPolygon> bands)
    {
        // The arc (center (100, -20) in Nazca coordinates, radius 20) ends at x = 120; its band
        // must extend 8 µm beyond the core's outer edge.
        double maxX = bands.Max(b => b.Points.Max(p => p.X));
        maxX.ShouldBeGreaterThan(120 + CompanionLayerTestCanvas.TrenchOffsetMicrometers);
    }
}
