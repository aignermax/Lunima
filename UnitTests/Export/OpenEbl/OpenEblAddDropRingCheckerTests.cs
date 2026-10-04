using CAP.Avalonia.Services;
using CAP.Avalonia.Services.OpenEblCheck;
using CAP_Core.Export;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// Issue #1359: the shipped <c>EBeam Add-Drop Ring.lun</c> example must survive the
/// vendored openEBL submission checks (<see cref="OpenEblSubmissionChecker"/>) with zero
/// errors — the add-drop ring is sized for the 605 × 410 µm openEBL die with its three
/// grating couplers in a 0° vertical 127 µm test array, so the export must be
/// submission-clean, not merely routable.
/// <para>
/// Gating: needs a Python with nazca + klayout + siepic_ebeam_pdk + SiEPIC (installed on the
/// CI runner by the "Install Nazca + KLayout + SiEPIC PDK" step); skips cleanly elsewhere.
/// Same pattern as <see cref="OpenEblSubmissionCheckerIntegrationTests"/>.
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class OpenEblAddDropRingCheckerTests
{
    [SkippableFact]
    public async Task EBeamAddDropRingExample_Checker_ReportsZeroErrorsOnBothChecks()
    {
        var python = await OpenEblMziReadinessTests.FindOpenEblVerificationPythonAsync();
        Skip.If(python == null, "No Python with nazca + klayout + siepic_ebeam_pdk + SiEPIC (expected on CI).");

        var dir = Path.Combine(Path.GetTempPath(), "lunima-openebl-ring-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var gdsPath = await ExportRingToGdsAsync(python, dir);

            var checker = new OpenEblSubmissionChecker(ProcessLaunchFactory.CreateDefault(), pythonPath: python);
            var report = await checker.CheckAsync(gdsPath);

            report.Status.ShouldBe(OpenEblCheckStatus.Passed,
                $"submission output:\n{report.SubmissionOutput}\nverification output:\n{report.VerificationOutput}");
            report.SubmissionChecksPassed.ShouldBeTrue();
            report.VerificationPassed.ShouldBeTrue();
            report.SubmissionErrorCount.ShouldBe(0);
            report.VerificationErrorCount.ShouldBe(0);
            report.Errors.ShouldBeEmpty();
            report.DieBoundingBox.ShouldNotBeNull();
            report.DieBoundingBox.WidthMicrometers.ShouldBeLessThanOrEqualTo(605.0);
            report.DieBoundingBox.HeightMicrometers.ShouldBeLessThanOrEqualTo(410.0);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp cleanup best effort */ }
        }
    }

    private static async Task<string> ExportRingToGdsAsync(string python, string workDir)
    {
        var examplePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(),
            EBeamAddDropRingExampleAuthoringTests.ExampleFileName);
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(examplePath);

        var skippedConnections = new List<string>();
        var exportWarnings = new List<string>();
        var script = new SimpleNazcaExporter().Export(
            canvas, skippedConnections: skippedConnections, exportWarnings: exportWarnings);
        skippedConnections.ShouldBeEmpty("every ring route must be real, exportable geometry");
        exportWarnings.ShouldBeEmpty();

        var scriptPath = Path.Combine(workDir, "design.py");
        await File.WriteAllTextAsync(scriptPath, script);
        var run = await SiepicRealGeometryExportTests.RunPythonAsync(python, workDir, scriptPath);
        run.ExitCode.ShouldBe(0, $"nazca export script failed:\n{run.StdOut}\n{run.StdErr}");
        var gdsPath = Path.ChangeExtension(scriptPath, ".gds");
        File.Exists(gdsPath).ShouldBeTrue($"script did not write {gdsPath}:\n{run.StdOut}");
        return gdsPath;
    }
}
