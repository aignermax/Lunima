using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// openEBL gap #3 (#1309), script level: a design built entirely from the bundled
/// SiEPIC EBeam PDK must export onto EBeam layers — waveguide interconnect on Si 1/0
/// with the PDK's 0.5 µm strip width, no nazca default interconnect layer (1111/0),
/// no demofab bb_body frame (1003/0); pin labels stay on PinRec (1/10). Mixed designs
/// keep the legacy global interconnect byte-identically.
/// </summary>
public class OpenEblEBeamLayerConformanceTests
{
    [Fact]
    public void Export_EBeamOnlyDesign_RoutesInterconnectOnEBeamLayer()
    {
        var canvas = EBeamCanvasBuilder.CreateWithWaveguide();
        EBeamCanvasBuilder.AddSiepicParametricStraight(canvas);

        var script = new SimpleNazcaExporter().Export(canvas);

        // The global interconnect and every routed segment carry the EBeam process
        // cross-section stamped onto the PDK components' pins (0.5 µm on Si 1/0).
        script.ShouldContain("WG_WIDTH = 0.5");
        script.ShouldContain("WG_LAYER = 1");
        script.ShouldContain("ic = Interconnect(width=WG_WIDTH, radius=BEND_RADIUS, layer=WG_LAYER)");
        script.ShouldContain("nd.strt(length=127.00, width=0.5, layer=1)");
        // Nothing outside the EBeam layer table: no nazca default interconnect
        // layer, no demofab bb_body frame — pin labels stay on PinRec (1, 10).
        script.ShouldNotContain("1111");
        script.ShouldNotContain("1003");
        script.ShouldContain("layer=(1, 10)");
    }

    [Fact]
    public void Export_MixedDesign_KeepsLegacyGlobalInterconnect()
    {
        var canvas = EBeamCanvasBuilder.CreateWithWaveguide();
        EBeamCanvasBuilder.AddSiepicParametricStraight(canvas);
        canvas.AddComponent(TestComponentFactory.CreateBasicComponent(), "DEMO");

        var script = new SimpleNazcaExporter().Export(canvas);

        script.ShouldContain("ic = Interconnect(width=WG_WIDTH, radius=BEND_RADIUS)");
        script.ShouldNotContain("WG_LAYER");
        script.ShouldContain("nd.strt(length=127.00).put(");
        // The demofab bb_body frame stays for designs that are not EBeam-only.
        script.ShouldContain("(1003, 0)");
    }
}

/// <summary>Builds the small in-test EBeam designs of the openEBL layer-conformance tests.</summary>
internal static class EBeamCanvasBuilder
{
    /// <summary>
    /// Two EBeam grating couplers (127 µm pitch, the DFT array spacing) joined by a
    /// straight waveguide — the smallest design that exercises routed interconnect.
    /// </summary>
    public static DesignCanvasViewModel CreateWithWaveguide()
    {
        var gcTemplate = TestPdkLoader.LoadFromPdk("siepic-ebeam-pdk.json")
            .First(t => t.NazcaFunctionName == "ebeam_gc_te1550");
        var canvas = new DesignCanvasViewModel();
        var gc1 = ComponentTemplates.CreateFromTemplate(gcTemplate, 0, 0);
        var gc2 = ComponentTemplates.CreateFromTemplate(gcTemplate, 127, 0);
        canvas.AddComponent(gc1, "GC1");
        canvas.AddComponent(gc2, "GC2");

        var from = gc1.PhysicalPins[0];
        var to = gc2.PhysicalPins[0];
        var (x1, y1) = from.GetAbsolutePosition();
        var (x2, y2) = to.GetAbsolutePosition();
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y1, x2, y2, 0));
        canvas.ConnectPinsWithCachedRoute(from, to, path).ShouldNotBeNull();
        return canvas;
    }

    /// <summary>
    /// A parametric-straight SiEPIC component (process-stamped pins): the only stub
    /// shape outside the EBeam layer table is its demofab bb_body frame.
    /// </summary>
    public static void AddSiepicParametricStraight(DesignCanvasViewModel canvas)
    {
        var strt = TestComponentFactory.CreateBasicComponent();
        strt.Identifier = "STRT1";
        strt.NazcaFunctionName = "ebeam_strt_custom";
        strt.NazcaModuleName = "siepic_ebeam_pdk";
        strt.NazcaFunctionParameters = "length=100E-6";
        strt.PhysicalPins.Clear();
        strt.PhysicalPins.Add(new PhysicalPin
        {
            Name = "in",
            ParentComponent = strt,
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 125,
            AngleDegrees = 180,
            WaveguideWidthMicrometers = 0.5,
            Layer = 1,
        });
        strt.PhysicalPins.Add(new PhysicalPin
        {
            Name = "out",
            ParentComponent = strt,
            OffsetXMicrometers = 250,
            OffsetYMicrometers = 125,
            AngleDegrees = 0,
            WaveguideWidthMicrometers = 0.5,
            Layer = 1,
        });
        canvas.AddComponent(strt, "STRT1");
    }
}

/// <summary>
/// openEBL gap #3 (#1309), end to end: the two-grating-coupler EBeam design exported
/// via <see cref="SimpleNazcaExporter"/> and rendered by real nazca must pass the
/// klayout-only port of openEBL's submission checks (shared with
/// <see cref="OpenEblMziReadinessTests"/>) with zero layer-conformity errors.
/// Gating: needs a Python with nazca + klayout + siepic_ebeam_pdk (installed on the
/// CI runner); skips cleanly elsewhere.
/// </summary>
[Trait("Category", "Slow")]
public class OpenEblEBeamSubmissionCheckTests
{
    [SkippableFact]
    public async Task EBeamOnlyDesign_OpenEblSubmissionChecks_ReportZeroLayerConformityErrors()
    {
        var python = await OpenEblMziReadinessTests.FindOpenEblCheckPythonAsync();
        Skip.If(python == null, "No Python with nazca + klayout + siepic_ebeam_pdk (expected on CI).");

        var canvas = EBeamCanvasBuilder.CreateWithWaveguide();
        var skippedConnections = new List<string>();
        var exportWarnings = new List<string>();
        var script = new SimpleNazcaExporter().Export(
            canvas, skippedConnections: skippedConnections, exportWarnings: exportWarnings);
        skippedConnections.ShouldBeEmpty();
        exportWarnings.ShouldBeEmpty();

        var dir = Path.Combine(Path.GetTempPath(), "lunima-openebl-ebeam-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var scriptPath = Path.Combine(dir, "ebeam_pair.py");
            await File.WriteAllTextAsync(scriptPath, script);
            var run = await SiepicRealGeometryExportTests.RunPythonAsync(python, dir, scriptPath);
            run.ExitCode.ShouldBe(0, $"nazca export script failed:\n{run.StdOut}\n{run.StdErr}");
            var gdsPath = Path.ChangeExtension(scriptPath, ".gds");
            File.Exists(gdsPath).ShouldBeTrue($"script did not write {gdsPath}:\n{run.StdOut}");

            var checkerPath = Path.Combine(dir, "openebl_submission_check.py");
            await File.WriteAllTextAsync(checkerPath, OpenEblMziReadinessTests.SubmissionCheckerScript);
            var check = await SiepicRealGeometryExportTests.RunPythonAsync(python, dir, checkerPath, gdsPath);
            check.ExitCode.ShouldBe(0, $"submission-check port crashed:\n{check.StdOut}\n{check.StdErr}");
            var output = check.StdOut;
            var errorCount = int.Parse(output.TrimEnd().Split('\n').Last().Trim());

            output.ShouldContain("Top cell: ConnectAPIC_Design");
            output.ShouldNotContain("is not defined in the PDK",
                customMessage: "an EBeam-only design must export onto EBeam layers only (gap #3)");
            errorCount.ShouldBe(0, $"submission check must pass cleanly:\n{output}");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp cleanup best effort */ }
        }
    }
}
