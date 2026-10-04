using CAP.Avalonia.Services.OpenEblCheck;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// Unit tests for <see cref="OpenEblSubmissionChecker"/> that need no real Python toolchain:
/// a nonexistent interpreter must yield <see cref="OpenEblCheckStatus.ToolchainMissing"/>
/// with the pip hint (never a crash, never a false pass), a missing scripts directory must
/// yield <see cref="OpenEblCheckStatus.ScriptsMissing"/>, and the report parser must turn
/// both scripts' stdout into typed entries.
/// </summary>
public class OpenEblSubmissionCheckerTests
{
    [Fact]
    public async Task CheckAsync_NonexistentInterpreter_ReturnsToolchainMissingWithPipHint()
    {
        var gdsPath = Path.Combine(Path.GetTempPath(), "lunima-openebl-missing-" + Guid.NewGuid().ToString("N") + ".gds");
        await File.WriteAllTextAsync(gdsPath, "not a real gds");
        try
        {
            var checker = new OpenEblSubmissionChecker(
                ProcessLaunchFactory.CreateDefault(),
                pythonPath: Path.Combine(Path.GetTempPath(), "lunima-no-such-python.exe"));

            var report = await checker.CheckAsync(gdsPath);

            report.Status.ShouldBe(OpenEblCheckStatus.ToolchainMissing);
            report.ToolchainMessage.ShouldNotBeNullOrEmpty();
            report.ToolchainMessage.ShouldContain("pip install klayout siepic_ebeam_pdk SiEPIC-Tools");
            report.SubmissionChecksPassed.ShouldBeFalse();
            report.VerificationPassed.ShouldBeFalse();
        }
        finally
        {
            File.Delete(gdsPath);
        }
    }

    [Fact]
    public async Task CheckAsync_MissingScriptsDirectory_ReturnsScriptsMissing()
    {
        var gdsPath = Path.Combine(Path.GetTempPath(), "lunima-openebl-nodir-" + Guid.NewGuid().ToString("N") + ".gds");
        await File.WriteAllTextAsync(gdsPath, "not a real gds");
        try
        {
            var checker = new OpenEblSubmissionChecker(
                ProcessLaunchFactory.CreateDefault(),
                scriptsDirectory: Path.Combine(Path.GetTempPath(), "lunima-no-such-scripts-dir"));

            var report = await checker.CheckAsync(gdsPath);

            report.Status.ShouldBe(OpenEblCheckStatus.ScriptsMissing);
            report.SubmissionChecksPassed.ShouldBeFalse();
        }
        finally
        {
            File.Delete(gdsPath);
        }
    }

    [Fact]
    public void ParseSubmissionOutput_DieSizeAndLayerErrors_BecomeTypedEntries()
    {
        var output = string.Join('\n',
            "Running openEBL submission checks (Lunima headless port) for file x.gds",
            "Top cell: ConnectAPIC_Design",
            "Error: Bounding box of selected layers (620.000 um x 300.000 um) exceeds allowed size 605.000 um x 410.000 um",
            "Performing Black Box cell replacement check",
            " - Number of unreplaced BB cells: 0",
            "Error: the layer 1111/0 in the design is not defined in the PDK.",
            "Error: the layer 1/20 in the design is not defined in the PDK.",
            "Floorplan (99/0) shapes: 0",
            "opt_in labels (10/0): 0",
            "16",
            "");

        var (errorCount, errors, boundingBox) = OpenEblReportParser.ParseSubmissionOutput(output);

        errorCount.ShouldBe(16);
        boundingBox.ShouldBe(new OpenEblDieBoundingBox(620.0, 300.0));
        errors.Count.ShouldBe(3);
        errors[0].Category.ShouldBe(OpenEblCheckCategories.DieSize);
        errors[0].Message.ShouldContain("exceeds allowed size");
        errors[1].Category.ShouldBe(OpenEblCheckCategories.LayerConformity);
        errors[1].Message.ShouldContain("1111/0");
        errors[2].Category.ShouldBe(OpenEblCheckCategories.LayerConformity);
        errors[2].Message.ShouldContain("1/20");
    }

    [Fact]
    public void ParseSubmissionOutput_CleanRun_HasNoErrorsAndParsesBoundingBox()
    {
        var output = string.Join('\n',
            "Top cell: ConnectAPIC_Design",
            "Bounding box of selected layers is 500.000 um x 200.000 um",
            "0",
            "");

        var (errorCount, errors, boundingBox) = OpenEblReportParser.ParseSubmissionOutput(output);

        errorCount.ShouldBe(0);
        errors.ShouldBeEmpty();
        boundingBox.ShouldBe(new OpenEblDieBoundingBox(500.0, 200.0));
    }

    [Fact]
    public void ParseSubmissionOutput_CrashedScript_HasNegativeErrorCount()
    {
        var (errorCount, _, _) = OpenEblReportParser.ParseSubmissionOutput("Traceback (most recent call last):\n");

        errorCount.ShouldBe(-1);
    }

    [Fact]
    public void ParseVerificationOutput_NonZeroCategories_BecomeTypedEntries()
    {
        var output = string.Join('\n',
            "Top cell: ConnectAPIC_Design",
            "category Disconnected pin: 2",
            "category Shapes outside component: 0",
            "category Waveguide: Path: 1",
            "3",
            "");

        var (errorCount, errors) = OpenEblReportParser.ParseVerificationOutput(output);

        errorCount.ShouldBe(3);
        errors.Count.ShouldBe(2);
        errors[0].Category.ShouldBe("Disconnected pin");
        errors[0].Message.ShouldContain("2 error(s)");
        errors[1].Category.ShouldBe("Waveguide: Path");
    }

    [Fact]
    public void ParseVerificationOutput_UnknownError_BecomesVerificationCrashEntry()
    {
        var output = "Top cell: X\nUnknown error occurred\n# underlying Exception: boom\ncategory Disconnected pin: 0\ncategory Shapes outside component: 0\n1\n";

        var (errorCount, errors) = OpenEblReportParser.ParseVerificationOutput(output);

        errorCount.ShouldBe(1);
        errors.Count.ShouldBe(1);
        errors[0].Category.ShouldBe(OpenEblCheckCategories.VerificationCrash);
    }
}
