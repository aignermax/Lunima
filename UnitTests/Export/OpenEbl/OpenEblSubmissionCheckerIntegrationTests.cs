using CAP.Avalonia.Services;
using CAP.Avalonia.Services.OpenEblCheck;
using CAP_Core.Export;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// Issue #1350: the headless <see cref="OpenEblSubmissionChecker"/> service runs the vendored
/// openEBL check scripts (<c>scripts/openebl/</c>) against real exports and returns a typed
/// report — the same proof the <see cref="OpenEblMziReadinessTests"/> pins, now available to
/// the app instead of only the test suite.
/// <para>
/// Gating: needs a Python with nazca + klayout + siepic_ebeam_pdk + SiEPIC (installed on the
/// CI runner by the "Install Nazca + KLayout + SiEPIC PDK" step); skips cleanly elsewhere.
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class OpenEblSubmissionCheckerIntegrationTests
{
    [SkippableFact]
    public async Task EBeamMziExample_Checker_ReportsZeroErrorsOnBothChecks()
    {
        var python = await OpenEblMziReadinessTests.FindOpenEblVerificationPythonAsync();
        Skip.If(python == null, "No Python with nazca + klayout + siepic_ebeam_pdk + SiEPIC (expected on CI).");

        var dir = Path.Combine(Path.GetTempPath(), "lunima-openebl-svc-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var gdsPath = await ExportExampleToGdsAsync(python, "EBeam Mach-Zehnder Interferometer.lun", dir);

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

    [SkippableFact]
    public async Task DemoMziExample_Checker_ListsDieSizeAndLayerErrorsAsTypedEntries()
    {
        var python = await OpenEblMziReadinessTests.FindOpenEblVerificationPythonAsync();
        Skip.If(python == null, "No Python with nazca + klayout + siepic_ebeam_pdk + SiEPIC (expected on CI).");

        var dir = Path.Combine(Path.GetTempPath(), "lunima-openebl-svc-demo-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var gdsPath = await ExportExampleToGdsAsync(python, "Mach-Zehnder Interferometer.lun", dir);

            var checker = new OpenEblSubmissionChecker(ProcessLaunchFactory.CreateDefault(), pythonPath: python);
            var report = await checker.CheckAsync(gdsPath);

            // The Demo-PDK MZI is NOT openEBL-ready: ~620 µm wide on the checked layers
            // (over the 605 µm die width) and 15 non-EBeam layers — the same gap set
            // OpenEblMziReadinessTests pins, now as typed report entries.
            report.Status.ShouldBe(OpenEblCheckStatus.Failed,
                $"submission output:\n{report.SubmissionOutput}");
            report.SubmissionChecksPassed.ShouldBeFalse();
            report.SubmissionErrorCount.ShouldBe(16);

            var dieSizeErrors = report.Errors.Where(e => e.Category == OpenEblCheckCategories.DieSize).ToList();
            dieSizeErrors.Count.ShouldBe(1);
            dieSizeErrors[0].Message.ShouldContain("exceeds allowed size 605.000 um x 410.000 um");

            report.Errors.Count(e => e.Category == OpenEblCheckCategories.LayerConformity).ShouldBe(15);
            report.Errors.ShouldContain(e =>
                e.Category == OpenEblCheckCategories.LayerConformity && e.Message.Contains("1111/0"));

            report.DieBoundingBox.ShouldNotBeNull();
            report.DieBoundingBox.WidthMicrometers.ShouldBeGreaterThan(605.0);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp cleanup best effort */ }
        }
    }

    /// <summary>
    /// Loads a shipped example through the real load path, exports it with the app's own
    /// exporter and runs the script under real nazca, returning the produced GDS path.
    /// </summary>
    private static async Task<string> ExportExampleToGdsAsync(string python, string exampleFileName, string workDir)
    {
        var examplePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), exampleFileName);
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(examplePath);

        var skippedConnections = new List<string>();
        var exportWarnings = new List<string>();
        var script = new SimpleNazcaExporter().Export(
            canvas, skippedConnections: skippedConnections, exportWarnings: exportWarnings);
        skippedConnections.ShouldBeEmpty("every MZI route must be real, exportable geometry");
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
