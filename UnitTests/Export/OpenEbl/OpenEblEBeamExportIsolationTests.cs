using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;
using Xunit;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// Issue #1447 — <see cref="OpenEblEBeamLayerConformanceTests.Export_EBeamOnlyDesign_RoutesInterconnectOnEBeamLayer"/>
/// failed once in an integrated non-Slow run (layer 1003 in the script) while passing
/// in isolation, i.e. some earlier test left shared state behind that flipped the
/// EBeam export profile. These tests pin order-independence: an EBeam-only export
/// must produce the EBeam layer mapping no matter which PDK loads and legacy
/// (mixed/demo) exports ran before it in the same process. If shared state ever
/// leaks into the export path again, this test fails deterministically — and the
/// profile diagnostic in <see cref="OpenEblEBeamLayerConformanceTests"/> names the
/// component or pin the leak came through.
/// </summary>
public class OpenEblEBeamExportIsolationTests
{
    [Fact]
    public void EBeamExport_StaysOnEBeamLayers_AfterMixedDesignExportWithDemofabFrame()
    {
        // Reference export of a fresh EBeam-only design.
        var reference = ExportFreshEBeamDesign(out _);
        AssertEBeamLayerMapping(reference);

        // Ambient work: a mixed design takes the LEGACY path — global interconnect
        // without WG_LAYER and the demofab bb_body frame (1003, 0) on the
        // parametric-straight stub. If any of that state outlived the export, the
        // next EBeam export would inherit it.
        var mixedCanvas = EBeamCanvasBuilder.CreateWithWaveguide();
        EBeamCanvasBuilder.AddSiepicParametricStraight(mixedCanvas);
        mixedCanvas.AddComponent(TestComponentFactory.CreateBasicComponent(), "DEMO");
        var mixedScript = new SimpleNazcaExporter().Export(mixedCanvas);
        mixedScript.ShouldContain("(1003, 0)"); // the frame that must not leak
        mixedScript.ShouldNotContain("WG_LAYER");

        // A second EBeam-only design built AFTER the legacy export must resolve the
        // profile and stay on the EBeam layer table.
        var again = ExportFreshEBeamDesign(out var freshCanvas);
        SiepicEBeamExportProfile.Resolve(freshCanvas)
            .ShouldNotBeNull(OpenEblEBeamLayerConformanceDiagnostics.Describe(freshCanvas));
        AssertEBeamLayerMapping(again);
    }

    [Fact]
    public void EBeamExport_StaysOnEBeamLayers_AfterLoadingEveryBundledPdkTemplate()
    {
        // Loading all bundled PDKs (demo + SiEPIC) exercises every registry/converter
        // path the exporter could read through.
        TestPdkLoader.LoadAllTemplates().ShouldNotBeEmpty();

        var script = ExportFreshEBeamDesign(out var canvas);
        SiepicEBeamExportProfile.Resolve(canvas)
            .ShouldNotBeNull(OpenEblEBeamLayerConformanceDiagnostics.Describe(canvas));
        AssertEBeamLayerMapping(script);
    }

    private static string ExportFreshEBeamDesign(out DesignCanvasViewModel canvas)
    {
        canvas = EBeamCanvasBuilder.CreateWithWaveguide();
        EBeamCanvasBuilder.AddSiepicParametricStraight(canvas);
        return new SimpleNazcaExporter().Export(canvas);
    }

    private static void AssertEBeamLayerMapping(string script)
    {
        script.ShouldContain("WG_WIDTH = 0.5");
        script.ShouldContain("WG_LAYER = 1");
        script.ShouldContain("nd.strt(length=127.00, width=0.5, layer=1)");
        script.ShouldNotContain("1111");
        script.ShouldNotContain("1003");
    }
}
