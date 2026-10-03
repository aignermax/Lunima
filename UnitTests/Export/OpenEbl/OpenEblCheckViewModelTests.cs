using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Services.OpenEblCheck;
using CAP.Avalonia.ViewModels.Export.OpenEbl;
using Shouldly;
using Xunit;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// Tests for <see cref="OpenEblCheckViewModel"/> (issue #1361): the export+check flow with a
/// fake checker (pass / fail with errors / toolchain missing / cancel), asynchronous command
/// execution, file-name proposal, and the category-label localization mapping.
/// </summary>
public class OpenEblCheckViewModelTests
{
    public OpenEblCheckViewModelTests()
    {
        // Status-text assertions depend on the process-wide locale.
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
    }

    private static OpenEblCheckReport PassedReport() => new()
    {
        Status = OpenEblCheckStatus.Passed,
        SubmissionChecksPassed = true,
        VerificationPassed = true,
        SubmissionErrorCount = 0,
        VerificationErrorCount = 0,
        DieBoundingBox = new OpenEblDieBoundingBox(605.0, 410.0),
    };

    private static OpenEblCheckReport FailedReport() => new()
    {
        Status = OpenEblCheckStatus.Failed,
        SubmissionChecksPassed = false,
        VerificationPassed = true,
        SubmissionErrorCount = 2,
        VerificationErrorCount = 0,
        Errors = new[]
        {
            new OpenEblCheckError(OpenEblCheckCategories.TopCell, "Layout has 2 top cells."),
            new OpenEblCheckError(
                OpenEblCheckCategories.DieSize,
                "Die size exceeds allowed size 605 um x 410 um",
                XMicrometers: 350.0, YMicrometers: 225.0),
        },
    };

    [Fact]
    public async Task ExportAndCheck_PassedReport_ShowsPassResult()
    {
        var checker = FakeOpenEblSubmissionChecker.Returning(PassedReport());
        var vm = OpenEblCheckTestDoubles.CreateViewModel(
            OpenEblCheckTestDoubles.CanvasWithComponent(), new FakeGdsExportService(), checker);

        await vm.ExportAndCheckCommand.ExecuteAsync(null);

        vm.HasResult.ShouldBeTrue();
        vm.Headline.ShouldBe("All checks passed — ready for openEBL submission");
        vm.ShowCheckOutcomes.ShouldBeTrue();
        vm.SubmissionPassed.ShouldBeTrue();
        vm.VerificationPassed.ShouldBeTrue();
        vm.Errors.ShouldBeEmpty();
        vm.DieSizeText.ShouldBe("605 × 410 µm");
        vm.GdsPathText.ShouldContain(".gds");
        checker.LastGdsPath.ShouldNotBeNull();
        Path.GetFileName(checker.LastGdsPath!).ShouldBe("EBeam_anonymous_design.gds");
    }

    [Fact]
    public async Task ExportAndCheck_FailedReport_ListsTypedErrors()
    {
        var checker = FakeOpenEblSubmissionChecker.Returning(FailedReport());
        var vm = OpenEblCheckTestDoubles.CreateViewModel(
            OpenEblCheckTestDoubles.CanvasWithComponent(), new FakeGdsExportService(), checker);

        await vm.ExportAndCheckCommand.ExecuteAsync(null);

        vm.HasResult.ShouldBeTrue();
        vm.Headline.ShouldBe("Checks failed — see the errors below");
        vm.SubmissionPassed.ShouldBeFalse();
        vm.VerificationPassed.ShouldBeTrue();
        vm.Errors.Count.ShouldBe(2);
        vm.Errors[0].CategoryLabel.ShouldBe("Top cell");
        vm.Errors[0].HasLocation.ShouldBeFalse();
        vm.Errors[1].CategoryLabel.ShouldBe("Die size");
        vm.Errors[1].Location.ShouldBe("(350 µm, 225 µm)");
    }

    [Fact]
    public async Task ExportAndCheck_ToolchainMissing_ShowsPipHintWithCopyButton()
    {
        var report = new OpenEblCheckReport
        {
            Status = OpenEblCheckStatus.ToolchainMissing,
            SubmissionChecksPassed = false,
            VerificationPassed = false,
            ToolchainMessage = "The openEBL check needs a Python toolchain. Install them with: pip install …",
        };
        var checker = FakeOpenEblSubmissionChecker.Returning(report);
        var vm = OpenEblCheckTestDoubles.CreateViewModel(
            OpenEblCheckTestDoubles.CanvasWithComponent(), new FakeGdsExportService(), checker);
        string? copied = null;
        vm.CopyToClipboard = text => { copied = text; return Task.CompletedTask; };

        await vm.ExportAndCheckCommand.ExecuteAsync(null);

        vm.HasResult.ShouldBeTrue();
        vm.IsToolchainMissing.ShouldBeTrue();
        vm.Headline.ShouldBe("Python toolchain for the check is missing");
        vm.ToolchainMessage.ShouldContain("pip install");
        vm.ShowCheckOutcomes.ShouldBeFalse();

        await vm.CopyPipHintCommand.ExecuteAsync(null);
        copied.ShouldBe(OpenEblPythonLocator.PipInstallHint);
    }

    [Fact]
    public async Task ExportAndCheck_Cancel_StopsTheRunAndReportsCancellation()
    {
        var checkerStarted = new TaskCompletionSource();
        var checker = new FakeOpenEblSubmissionChecker(async ct =>
        {
            checkerStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);   // throws OperationCanceledException on cancel
            throw new InvalidOperationException("unreachable");
        });
        var vm = OpenEblCheckTestDoubles.CreateViewModel(
            OpenEblCheckTestDoubles.CanvasWithComponent(), new FakeGdsExportService(), checker);

        var run = vm.ExportAndCheckCommand.ExecuteAsync(null);
        await checkerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        vm.IsChecking.ShouldBeTrue();

        vm.CancelCommand.Execute(null);
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        vm.IsChecking.ShouldBeFalse();
        vm.HasResult.ShouldBeFalse();
        vm.StatusText.ShouldBe("Check cancelled.");
    }

    [Fact]
    public async Task ExportAndCheck_RunsAsynchronously_UiThreadStaysFree()
    {
        var releaseCheck = new TaskCompletionSource();
        var checker = new FakeOpenEblSubmissionChecker(async ct =>
        {
            await releaseCheck.Task;   // blocks the check until the test releases it
            return PassedReport();
        });
        var vm = OpenEblCheckTestDoubles.CreateViewModel(
            OpenEblCheckTestDoubles.CanvasWithComponent(), new FakeGdsExportService(), checker);

        var run = vm.ExportAndCheckCommand.ExecuteAsync(null);

        // The command returns while the check is still in flight — the UI thread is free.
        run.IsCompleted.ShouldBeFalse();
        vm.IsChecking.ShouldBeTrue();

        releaseCheck.SetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        vm.IsChecking.ShouldBeFalse();
        vm.HasResult.ShouldBeTrue();
    }

    [Fact]
    public async Task ExportAndCheck_ExportFails_ShowsErrorAndSkipsCheck()
    {
        var checker = FakeOpenEblSubmissionChecker.Returning(PassedReport());
        var gdsExport = new FakeGdsExportService(success: false);
        var vm = OpenEblCheckTestDoubles.CreateViewModel(
            OpenEblCheckTestDoubles.CanvasWithComponent(), gdsExport, checker);

        await vm.ExportAndCheckCommand.ExecuteAsync(null);

        vm.HasResult.ShouldBeTrue();
        vm.Headline.ShouldBe("GDS export failed");
        vm.DetailMessage.ShouldBe("simulated export failure");
        checker.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task ExportAndCheck_EmptyCanvas_ReportsNothingToExport()
    {
        var gdsExport = new FakeGdsExportService();
        var checker = FakeOpenEblSubmissionChecker.Returning(PassedReport());
        var vm = OpenEblCheckTestDoubles.CreateViewModel(
            new CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel(), gdsExport, checker);

        await vm.ExportAndCheckCommand.ExecuteAsync(null);

        vm.StatusText.ShouldBe("Nothing to check — add components to the design first.");
        vm.HasResult.ShouldBeFalse();
        gdsExport.CallCount.ShouldBe(0);
        checker.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task ExportAndCheck_InvalidFileName_ReportsError()
    {
        var gdsExport = new FakeGdsExportService();
        var checker = FakeOpenEblSubmissionChecker.Returning(PassedReport());
        var vm = OpenEblCheckTestDoubles.CreateViewModel(
            OpenEblCheckTestDoubles.CanvasWithComponent(), gdsExport, checker);
        vm.FileName = "bad/name.gds";   // '/' is an invalid file-name char on every OS

        await vm.ExportAndCheckCommand.ExecuteAsync(null);

        vm.StatusText.ShouldBe("Please enter a valid .gds file name.");
        gdsExport.CallCount.ShouldBe(0);
    }

    [Fact]
    public void FileName_IsProposedFromUsernameAndDesignName()
    {
        var vm = OpenEblCheckTestDoubles.CreateViewModel(
            OpenEblCheckTestDoubles.CanvasWithComponent(),
            new FakeGdsExportService(),
            FakeOpenEblSubmissionChecker.Returning(PassedReport()));

        vm.Username = "jsmith";
        vm.DesignName = "My MZI v2";

        vm.FileName.ShouldBe("EBeam_jsmith_My_MZI_v2.gds");
    }

    [Fact]
    public void FileName_KeepsUserEditWhenInputsChange()
    {
        var vm = OpenEblCheckTestDoubles.CreateViewModel(
            OpenEblCheckTestDoubles.CanvasWithComponent(),
            new FakeGdsExportService(),
            FakeOpenEblSubmissionChecker.Returning(PassedReport()));

        vm.FileName = "EBeam_custom_name.gds";
        vm.Username = "jsmith";

        vm.FileName.ShouldBe("EBeam_custom_name.gds");
    }

    [Fact]
    public async Task PrepareForOpen_PrefillsDesignNameAndResetsResult()
    {
        var checker = FakeOpenEblSubmissionChecker.Returning(PassedReport());
        var vm = OpenEblCheckTestDoubles.CreateViewModel(
            OpenEblCheckTestDoubles.CanvasWithComponent(), new FakeGdsExportService(), checker);
        await vm.ExportAndCheckCommand.ExecuteAsync(null);
        vm.HasResult.ShouldBeTrue();

        vm.PrepareForOpen("EBeam Mach-Zehnder Interferometer");

        vm.DesignName.ShouldBe("EBeam Mach-Zehnder Interferometer");
        vm.FileName.ShouldBe("EBeam_anonymous_EBeam_Mach-Zehnder_Interferometer.gds");
        vm.HasResult.ShouldBeFalse();
        vm.StatusText.ShouldBeEmpty();
    }

    [Fact]
    public void CategoryLabelFor_MapsKnownCategoriesAndFallsBackForRuleNames()
    {
        OpenEblCheckViewModel.CategoryLabelFor(OpenEblCheckCategories.Floorplan).ShouldBe("Floorplan");
        OpenEblCheckViewModel.CategoryLabelFor("some_layout_check_rule").ShouldBe("Check");
    }
}
