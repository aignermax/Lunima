using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;
using Xunit;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// Issue #1447 — <see cref="OpenEblEBeamLayerConformanceTests.Export_EBeamOnlyDesign_RoutesInterconnectOnEBeamLayer"/>
/// failed once in an integrated non-Slow run ("1003" in the script) while passing
/// in isolation. Root cause: NOT a layer/profile leak — the process-wide instance
/// counter in <c>ComponentTemplates.CreateFromTemplate</c> names placed components
/// "&lt;Template&gt;_&lt;n&gt;", the exporter writes those names into the script
/// (placement comments, port labels), and the old assertion matched the bare
/// substring "1003". Once enough components had been created earlier in the run,
/// an identifier like "Grating Coupler TE 1550_1003" tripped it. The assertions
/// now match layer tokens ("(1003," / "layer=1111") and
/// <see cref="EBeamExport_InstanceNamesContainingLayerDigits_StayConformant"/>
/// reproduces the polluted-counter state deterministically. The remaining tests
/// pin order-independence of the EBeam profile against legacy exports and PDK
/// loading, with the profile diagnostic naming any leaked input.
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

    [Fact]
    public void EBeamExport_InstanceNamesContainingLayerDigits_StayConformant()
    {
        var canvas = EBeamCanvasBuilder.CreateWithWaveguide();
        EBeamCanvasBuilder.AddSiepicParametricStraight(canvas);
        // Reproduce the #1447 failure state: after ~1000 earlier component creations
        // the process-wide instance counter yields exactly these names, which the
        // exporter writes into the script as placement comments and port labels.
        EBeamCanvasBuilder.FindComponentAtX(canvas, 0).Identifier = "Grating Coupler TE 1550_1003";
        EBeamCanvasBuilder.FindComponentAtX(canvas, 127).Identifier = "Grating Coupler TE 1550_1111";

        var script = new SimpleNazcaExporter().Export(canvas);

        // The digits land in the script (this is what the old bare substring
        // assertion tripped on) …
        script.ShouldContain("Grating Coupler TE 1550_1003");
        script.ShouldContain("Grating Coupler TE 1550_1111");
        // … while the profile resolves and the layer table stays EBeam-conformant.
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
        script.ShouldNotContain("layer=1111");
        script.ShouldNotContain("(1111,");
        script.ShouldNotContain("(1003,");
    }
}
