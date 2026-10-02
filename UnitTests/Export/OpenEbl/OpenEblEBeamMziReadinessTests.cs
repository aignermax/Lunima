using CAP.Avalonia.Services;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// openEBL readiness gate for the EBeam Mach-Zehnder example (issue #1310, gap #1
/// of the readiness report): exports the shipped
/// <c>EBeam Mach-Zehnder Interferometer.lun</c> — built only from the bundled
/// SiEPIC EBeam PDK, grating couplers at 0° in a vertical 127 µm-pitch array,
/// inside the 605 × 410 µm die — through the real nazca path and runs the same
/// headless port of openEBL's submission checks as
/// <see cref="OpenEblMziReadinessTests"/> against the produced GDS.
/// <para>
/// Unlike the Demo-PDK MZI, this export contains only real EBeam foundry cells,
/// so the die-size rule passes by construction. The pinned error count covers
/// what gap #1 deliberately did not fix and what has since landed: the
/// interconnect layer mapping (gap #3, #1309) and the floorplan/opt_in
/// conventions (gaps #5/#6, #1320 — pinned by the census lines below).
/// </para>
/// <para>
/// Gating: needs a Python with nazca + klayout + siepic_ebeam_pdk (installed on
/// the CI runner by the "Install Nazca + KLayout + SiEPIC PDK" step); skips
/// cleanly elsewhere. Setting LUNIMA_OPENEBL_ARTIFACT_DIR copies the script,
/// GDS and checker output there for inspection.
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class OpenEblEBeamMziReadinessTests
{
    private const string ExampleFileName = "EBeam Mach-Zehnder Interferometer.lun";

    [SkippableFact]
    public async Task EBeamMziExample_OpenEblSubmissionChecks_PinCurrentGaps()
    {
        var python = await OpenEblMziReadinessTests.FindOpenEblCheckPythonAsync();
        Skip.If(python == null, "No Python with nazca + klayout + siepic_ebeam_pdk (expected on CI).");

        // ── 1. Load the shipped example through the real load path ──
        var examplePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFileName);
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(examplePath);
        canvas.Components.Count.ShouldBe(4);
        canvas.Connections.Count.ShouldBe(4);

        // ── 2. Export with the app's own exporter; nothing may be skipped ──
        var skippedConnections = new List<string>();
        var exportWarnings = new List<string>();
        var script = new SimpleNazcaExporter().Export(
            canvas, skippedConnections: skippedConnections, exportWarnings: exportWarnings);
        skippedConnections.ShouldBeEmpty("every MZI route must be real, exportable geometry");
        exportWarnings.ShouldBeEmpty();

        // ── 3. Run the export under real nazca ──
        var dir = Path.Combine(Path.GetTempPath(), "lunima-openebl-ebeam-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var scriptPath = Path.Combine(dir, "ebeam_mzi.py");
            await File.WriteAllTextAsync(scriptPath, script);
            var run = await SiepicRealGeometryExportTests.RunPythonAsync(python, dir, scriptPath);
            run.ExitCode.ShouldBe(0, $"nazca export script failed:\n{run.StdOut}\n{run.StdErr}");
            var gdsPath = Path.ChangeExtension(scriptPath, ".gds");
            File.Exists(gdsPath).ShouldBeTrue($"script did not write {gdsPath}:\n{run.StdOut}");

            // ── 4. Run the headless port of openEBL's submission checks ──
            var checkerPath = Path.Combine(dir, "openebl_submission_check.py");
            await File.WriteAllTextAsync(checkerPath, OpenEblMziReadinessTests.SubmissionCheckerScript);
            var check = await SiepicRealGeometryExportTests.RunPythonAsync(python, dir, checkerPath, gdsPath);
            check.ExitCode.ShouldBe(0, $"submission-check port crashed:\n{check.StdOut}\n{check.StdErr}");
            var output = check.StdOut;
            var errorCount = int.Parse(output.TrimEnd().Split('\n').Last().Trim());

            OpenEblMziReadinessTests.CopyArtifacts(dir, scriptPath, gdsPath, checkerPath, output);

            // ── 5. Pin the current readiness state ──
            output.ShouldContain("Top cell: ConnectAPIC_Design");
            output.ShouldNotContain("does not have 1 top cell");
            // Gap #1 closed: the EBeam re-layout on the 127 µm GC grid fits the
            // openEBL die by construction — no 605 x 410 µm violation.
            output.ShouldNotContain("exceeds allowed size 605.000 um x 410.000 um");
            // The grating couplers export as the allow-listed foundry black-box cell.
            output.ShouldContain("black box cell: ebeam_gc_te1550");
            output.ShouldContain("Number of unreplaced BB cells: 0");

            // Gap #3 closed (#1309): the EBeam-only export routes on Si 1/0 and drops
            // the bb_body frame, so no layer-conformity error remains.
            output.ShouldNotContain("in the design is not defined in the PDK");
            // Gaps #5/#6 closed (#1320): the export carries the die floorplan box and
            // exactly one opt_in measurement label at the laser-injection GC (the
            // second GC is the detector — its laser is off), what the functional
            // verification (DFT.xml) requires. Census lines, not errors.
            output.ShouldContain("Floorplan (99/0) shapes: 1");
            output.ShouldContain("opt_in labels (10/0): 1");
            errorCount.ShouldBe(0, $"the EBeam MZI must pass the submission-check port:\n{output}");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp cleanup best effort */ }
        }
    }
}
