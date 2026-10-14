using CAP.Avalonia.Services;
using CAP_Core.Routing.WaveguideCompanions;
using Shouldly;
using Xunit;

namespace UnitTests.Export.WaveguideCompanions;

/// <summary>
/// The Nazca export maps process companion layers onto Nazca's xsection model: one
/// <c>nd.add_xsection</c> + <c>nd.add_layer2xsection(..., growx=…)</c> per distinct band set,
/// and every routed optical segment — straight and arc — drawn a second time through it.
/// </summary>
public class SimpleNazcaExporterCompanionLayerTests
{
    [Fact]
    public void Export_WithTrenchBand_DefinesNazcaXsectionWithGrowx()
    {
        var (canvas, _) = CompanionLayerTestCanvas.Create(CompanionLayerTestCanvas.TrenchSpec());

        var script = new SimpleNazcaExporter().Export(canvas);

        script.ShouldContain("nd.add_xsection('LUNIMA_WG_COMPANIONS_1')");
        script.ShouldContain(
            "nd.add_layer2xsection(xsection='LUNIMA_WG_COMPANIONS_1', layer=(2, 0), growx=8.0)");
    }

    [Fact]
    public void Export_WithTrenchBand_DrawsStraightAndArcSegmentsThroughCompanionXsection()
    {
        var (canvas, _) = CompanionLayerTestCanvas.Create(CompanionLayerTestCanvas.TrenchSpec());

        var script = new SimpleNazcaExporter().Export(canvas);

        // Core first, then the same geometry through the companion xsection.
        script.ShouldContain("nd.strt(length=100.00).put(0.00, 0.00, 0.00)");
        script.ShouldContain("nd.strt(length=100.00, xs='LUNIMA_WG_COMPANIONS_1').put(0.00, 0.00, 0.00)");
        script.ShouldContain("nd.bend(radius=20.00, angle=-90.00).put(100.00, 0.00, 0.00)");
        script.ShouldContain("nd.bend(radius=20.00, angle=-90.00, xs='LUNIMA_WG_COMPANIONS_1').put(100.00, 0.00, 0.00)");
        script.ShouldContain("nd.strt(length=100.00, xs='LUNIMA_WG_COMPANIONS_1').put(120.00, -20.00, -90.00)");
        CountOccurrences(script, "xs='LUNIMA_WG_COMPANIONS_1'").ShouldBe(3);
    }

    [Fact]
    public void Export_WithoutProvider_EmitsCoreOnly()
    {
        var (canvas, _) = CompanionLayerTestCanvas.Create(spec: null);

        var script = new SimpleNazcaExporter().Export(canvas);

        script.ShouldNotContain("add_xsection");
        script.ShouldNotContain("xs='");
    }

    [Fact]
    public void Export_EmptySpec_EmitsCoreOnly()
    {
        var (canvas, _) = CompanionLayerTestCanvas.Create(WaveguideCompanionSpec.Empty);

        var script = new SimpleNazcaExporter().Export(canvas);

        script.ShouldNotContain("add_xsection");
        script.ShouldNotContain("xs='");
    }

    [Fact]
    public void Export_MetalTrace_GetsNoOpticalCompanionBands()
    {
        var (canvas, _) = CompanionLayerTestCanvas.Create(CompanionLayerTestCanvas.TrenchSpec(), electrical: true);

        var script = new SimpleNazcaExporter().Export(canvas);

        script.ShouldNotContain("add_xsection");
        script.ShouldNotContain("xs='");
    }

    [Fact]
    public void Export_ImportedSourceLayerConnection_KeepsItsOwnGeometryOnly()
    {
        var (canvas, connection) = CompanionLayerTestCanvas.Create(CompanionLayerTestCanvas.TrenchSpec());
        connection.SourceGdsLayer = 1;
        connection.SourceGdsDataType = 0;

        var script = new SimpleNazcaExporter().Export(canvas);

        script.ShouldNotContain("xs='");
    }

    [Fact]
    public void Export_AbsoluteWidthBand_ConvertsToGrowxFromCoreWidth()
    {
        var spec = new WaveguideCompanionSpec(new[]
        {
            new WaveguideCompanionLayer("CLAD", 22, 0, WaveguideCompanionRole.Cladding, null, 10.5),
        });
        var (canvas, connection) = CompanionLayerTestCanvas.Create(spec);
        connection.WidthMicrometers = 0.5;

        var script = new SimpleNazcaExporter().Export(canvas);

        script.ShouldContain("layer=(22, 0), growx=5.0)");
    }

    [Fact]
    public void Export_TwoBands_ShareOneXsection()
    {
        var spec = new WaveguideCompanionSpec(new[]
        {
            new WaveguideCompanionLayer("TRENCH", 2, 0, WaveguideCompanionRole.Trench, 8.0, null),
            new WaveguideCompanionLayer("KEEPOUT", 40, 1, WaveguideCompanionRole.KeepOut, 3.0, null),
        });
        var (canvas, _) = CompanionLayerTestCanvas.Create(spec);

        var script = new SimpleNazcaExporter().Export(canvas);

        script.ShouldContain("nd.add_layer2xsection(xsection='LUNIMA_WG_COMPANIONS_1', layer=(2, 0), growx=8.0)");
        script.ShouldContain("nd.add_layer2xsection(xsection='LUNIMA_WG_COMPANIONS_1', layer=(40, 1), growx=3.0)");
        script.ShouldNotContain("LUNIMA_WG_COMPANIONS_2");
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
