using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Services.OpenEblCheck;
using Shouldly;
using Xunit;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// Tests for the Lunima pre-flight of <c>OpenEblCheckViewModel</c> (issue #1375): unrouted or
/// blocked connections and DRC-lite errors surface before the external check runs — errors hold
/// the run until "Check anyway", warnings only annotate the external result, and a clean design
/// behaves exactly as before.
/// </summary>
public class OpenEblCheckPreflightTests
{
    public OpenEblCheckPreflightTests()
    {
        // Severity labels and headings depend on the process-wide locale.
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

    [Fact]
    public async Task ExportAndCheck_BlockedConnection_ShowsPreflightErrorAndSkipsExternalCheck()
    {
        var checker = FakeOpenEblSubmissionChecker.Returning(PassedReport());
        var gdsExport = new FakeGdsExportService();
        var vm = OpenEblCheckTestDoubles.CreateViewModel(
            OpenEblCheckTestDoubles.CanvasWithBlockedConnection(), gdsExport, checker);

        await vm.ExportAndCheckCommand.ExecuteAsync(null);

        vm.HasPreflightErrors.ShouldBeTrue();
        vm.PreflightIssues.ShouldNotBeEmpty();
        vm.PreflightIssues.ShouldAllBe(issue => issue.IsError);
        vm.PreflightIssues[0].Message.ShouldContain("C1.out");
        vm.PreflightIssues[0].Message.ShouldContain("C2.in");
        vm.PreflightIssues[0].SeverityLabel.ShouldBe("Error");
        checker.CallCount.ShouldBe(0, "the external check must not run while pre-flight errors hold it");
        gdsExport.CallCount.ShouldBe(0, "no GDS is exported before the student confirms");
        vm.HasResult.ShouldBeFalse();
    }

    [Fact]
    public async Task ExportAndCheck_UnroutedConnection_ShowsPreflightErrorAndSkipsExternalCheck()
    {
        var checker = FakeOpenEblSubmissionChecker.Returning(PassedReport());
        var vm = OpenEblCheckTestDoubles.CreateViewModel(
            OpenEblCheckTestDoubles.CanvasWithUnroutedConnection(), new FakeGdsExportService(), checker);

        await vm.ExportAndCheckCommand.ExecuteAsync(null);

        vm.HasPreflightErrors.ShouldBeTrue();
        vm.PreflightIssues.Count.ShouldBe(1);
        vm.PreflightIssues[0].Message.ShouldBe("Unrouted connection: C1.out to C2.in");
        checker.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task CheckAnyway_AfterPreflightErrors_RunsExternalCheckAndKeepsList()
    {
        var checker = FakeOpenEblSubmissionChecker.Returning(PassedReport());
        var vm = OpenEblCheckTestDoubles.CreateViewModel(
            OpenEblCheckTestDoubles.CanvasWithBlockedConnection(), new FakeGdsExportService(), checker);
        await vm.ExportAndCheckCommand.ExecuteAsync(null);
        checker.CallCount.ShouldBe(0);

        await vm.CheckAnywayCommand.ExecuteAsync(null);

        checker.CallCount.ShouldBe(1);
        vm.HasResult.ShouldBeTrue();
        vm.HasPreflightErrors.ShouldBeTrue("the pre-flight list stays visible above the external result");
        vm.PreflightIssues.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task ExportAndCheck_WarningOnly_RunsThroughAndShowsWarningsAboveResult()
    {
        var checker = FakeOpenEblSubmissionChecker.Returning(PassedReport());
        var vm = OpenEblCheckTestDoubles.CreateViewModel(
            OpenEblCheckTestDoubles.CanvasWithWarningConnection(), new FakeGdsExportService(), checker);

        await vm.ExportAndCheckCommand.ExecuteAsync(null);

        checker.CallCount.ShouldBe(1, "warnings must not hold the external check");
        vm.HasResult.ShouldBeTrue();
        vm.HasPreflightErrors.ShouldBeFalse();
        vm.HasPreflightWarnings.ShouldBeTrue();
        vm.PreflightIssues.ShouldAllBe(issue => !issue.IsError);
        vm.PreflightIssues[0].SeverityLabel.ShouldBe("Warning");
    }

    [Fact]
    public async Task ExportAndCheck_CleanDesign_RunsCheckerOnceWithoutPreflightList()
    {
        var checker = FakeOpenEblSubmissionChecker.Returning(PassedReport());
        var vm = OpenEblCheckTestDoubles.CreateViewModel(
            OpenEblCheckTestDoubles.CanvasWithComponent(), new FakeGdsExportService(), checker);

        await vm.ExportAndCheckCommand.ExecuteAsync(null);

        checker.CallCount.ShouldBe(1);
        vm.PreflightIssues.ShouldBeEmpty();
        vm.HasPreflightErrors.ShouldBeFalse();
        vm.HasPreflightWarnings.ShouldBeFalse();
        vm.HasResult.ShouldBeTrue();
    }

    [Fact]
    public async Task CheckAnyway_RunsAsynchronously_UiThreadStaysFree()
    {
        var releaseCheck = new TaskCompletionSource();
        var checker = new FakeOpenEblSubmissionChecker(async ct =>
        {
            await releaseCheck.Task;
            return PassedReport();
        });
        var vm = OpenEblCheckTestDoubles.CreateViewModel(
            OpenEblCheckTestDoubles.CanvasWithBlockedConnection(), new FakeGdsExportService(), checker);
        await vm.ExportAndCheckCommand.ExecuteAsync(null);

        var run = vm.CheckAnywayCommand.ExecuteAsync(null);

        run.IsCompleted.ShouldBeFalse("the command returns while the check is still in flight");
        vm.IsChecking.ShouldBeTrue();

        releaseCheck.SetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        vm.IsChecking.ShouldBeFalse();
        vm.HasResult.ShouldBeTrue();
    }

    [Fact]
    public async Task PrepareForOpen_ClearsPreflightState()
    {
        var checker = FakeOpenEblSubmissionChecker.Returning(PassedReport());
        var vm = OpenEblCheckTestDoubles.CreateViewModel(
            OpenEblCheckTestDoubles.CanvasWithBlockedConnection(), new FakeGdsExportService(), checker);
        await vm.ExportAndCheckCommand.ExecuteAsync(null);
        vm.HasPreflightErrors.ShouldBeTrue();

        vm.PrepareForOpen("design");

        vm.PreflightIssues.ShouldBeEmpty();
        vm.HasPreflightErrors.ShouldBeFalse();
        vm.HasPreflightWarnings.ShouldBeFalse();
    }
}
