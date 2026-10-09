using System.Globalization;
using System.Text.RegularExpressions;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Export;
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

        // #1447: this test once failed order-dependently — not a profile leak but
        // the bare "1003" substring assertion matching an instance name (see
        // OpenEblEBeamExportIsolationTests). Keep the up-front profile check with
        // a full state dump so a REAL profile leak would name the leaked input.
        SiepicEBeamExportProfile.Resolve(canvas)
            .ShouldNotBeNull(OpenEblEBeamLayerConformanceDiagnostics.Describe(canvas));

        var script = new SimpleNazcaExporter().Export(canvas);

        // The global interconnect and every routed segment carry the EBeam process
        // cross-section stamped onto the PDK components' pins (0.5 µm on Si 1/0).
        script.ShouldContain("WG_WIDTH = 0.5");
        script.ShouldContain("WG_LAYER = 1");
        script.ShouldContain("ic = Interconnect(width=WG_WIDTH, radius=BEND_RADIUS, layer=WG_LAYER)");
        script.ShouldContain("nd.strt(length=127.00, width=0.5, layer=1)");
        // Nothing outside the EBeam layer table: no nazca default interconnect
        // layer, no demofab bb_body frame — pin labels stay on PinRec (1, 10).
        // Match layer TOKENS, not bare digits: the process-wide instance counter
        // writes names like "Grating Coupler TE 1550_1003" into the script
        // (placement comments, port labels), so a bare ShouldNotContain("1003")
        // failed order-dependently in integrated runs (#1447).
        script.ShouldNotContain("layer=1111");
        script.ShouldNotContain("(1111,");
        script.ShouldNotContain("(1003,");
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
        // openEBL DFT markers are EBeam-only: no opt_in label, no floorplan box.
        script.ShouldNotContain("opt_in");
        script.ShouldNotContain("layer=(10, 0)");
        script.ShouldNotContain("layer=(99, 0)");
    }

    [Fact]
    public void Export_EBeamOnlyDesign_EmitsOptInLabelAtLaserInputGratingCoupler()
    {
        var canvas = EBeamCanvasBuilder.CreateWithWaveguide();
        // The second coupler is the detector: laser off (listen-only output, #690).
        EBeamCanvasBuilder.SetLaserEnabled(canvas, 127, false);

        var script = new SimpleNazcaExporter().Export(
            canvas, designName: "EBeam Mach-Zehnder Interferometer");

        var labels = FindOptInLabels(script);
        labels.Count.ShouldBe(1);
        labels[0].Text.ShouldBe("opt_in_TE_1550_device_EBeam_Mach_Zehnder_Interferometer");

        // The label anchors on the input GC's cell origin — distance 0, well inside
        // the 10 µm DFT tolerance — and far from the detector GC 127 µm away.
        var gc1 = EBeamCanvasBuilder.FindComponentAtX(canvas, 0);
        var origin = NazcaCoordinateMapper.GetCellPlacement(gc1, rawOverrideAnchor: null);
        var dx = labels[0].X - origin.X;
        var dy = labels[0].Y - origin.Y;
        Math.Sqrt(dx * dx + dy * dy).ShouldBeLessThanOrEqualTo(10.0);
    }

    [Fact]
    public void Export_EBeamOnlyDesign_TwoLaserInputs_GetUniqueOptInLabels()
    {
        var canvas = EBeamCanvasBuilder.CreateWithWaveguide();

        var script = new SimpleNazcaExporter().Export(canvas, designName: "chip");

        var labels = FindOptInLabels(script).Select(l => l.Text).ToList();
        labels.Count.ShouldBe(2);
        labels.Distinct().Count().ShouldBe(2);
        labels.ShouldContain("opt_in_TE_1550_device_chip");
        labels.ShouldContain("opt_in_TE_1550_device_chip_2");
    }

    [Fact]
    public void Export_EBeamOnlyDesign_WithoutDesignName_UsesTopCellNameInLabel()
    {
        var canvas = EBeamCanvasBuilder.CreateWithWaveguide();

        var script = new SimpleNazcaExporter().Export(canvas);

        FindOptInLabels(script).Select(l => l.Text)
            .ShouldContain("opt_in_TE_1550_device_ConnectAPIC_Design");
    }

    [Fact]
    public void Export_EBeamOnlyDesign_EmitsDieFloorplanBoxAroundDesign()
    {
        var canvas = EBeamCanvasBuilder.CreateWithWaveguide();

        var script = new SimpleNazcaExporter().Export(canvas);

        var boxes = Regex.Matches(
            script,
            @"nd\.Polygon\(points=\[\((-?[\d.]+),(-?[\d.]+)\),\((-?[\d.]+),(-?[\d.]+)\)," +
            @"\((-?[\d.]+),(-?[\d.]+)\),\((-?[\d.]+),(-?[\d.]+)\)\], layer=\(99, 0\)\)");
        boxes.Count.ShouldBe(1);

        double Coord(int group) => double.Parse(boxes[0].Groups[group].Value, CultureInfo.InvariantCulture);
        // Points are (x0,y0), (x1,y0), (x1,y1), (x0,y1) — the openEBL die, exactly.
        var x0 = Coord(1);
        var y0 = Coord(2);
        (Coord(3) - x0).ShouldBe(605.0, 0.001);
        (Coord(6) - y0).ShouldBe(410.0, 0.001);

        // The box's lower-left corner is the design bbox lower-left (nazca space:
        // min PhysicalX, negated max bottom edge), so the design sits inside it.
        // Tolerance covers the exporter's F2 coordinate rounding.
        var components = canvas.Components.Select(vm => vm.Component).ToList();
        x0.ShouldBe(components.Min(c => c.PhysicalX), 0.01);
        y0.ShouldBe(-components.Max(c => c.PhysicalY + c.HeightMicrometers), 0.01);
    }

    private static List<(string Text, double X, double Y)> FindOptInLabels(string script) =>
        Regex.Matches(
                script,
                @"nd\.Annotation\(text='(opt_in_TE_1550_device_[^']+)', layer=\(10, 0\)\)" +
                @"\.put\((-?[\d.]+), (-?[\d.]+)\)")
            .Select(m => (
                m.Groups[1].Value,
                double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)))
            .ToList();
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

    /// <summary>Returns the placed component whose left edge sits at the given X.</summary>
    public static Component FindComponentAtX(DesignCanvasViewModel canvas, double physicalX) =>
        canvas.Components.Select(vm => vm.Component).First(c => c.PhysicalX == physicalX);

    /// <summary>Switches a coupler's laser on (input) or off (listen-only output).</summary>
    public static void SetLaserEnabled(DesignCanvasViewModel canvas, double physicalX, bool enabled) =>
        FindComponentAtX(canvas, physicalX).LaserEnabled = enabled;

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

            var checkerPath = OpenEblScriptFiles.SubmissionCheckScriptPath;
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
