using CAP_Core.Export.Contracts;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP_Core.Export;
using Shouldly;

namespace UnitTests.Onboarding.FirstStepsTutorial;

public class ManufacturingTourViewModelTests
{
    [Fact]
    public async Task ExportStep_RequiresSuccessfulGds_NotScriptOrFailure()
    {
        var service = new ControlledExportService();
        var export = new GdsExportViewModel(service, new global::CAP_Core.Export.PythonDiscoveryService());
        var tour = new ManufacturingTourViewModel(export);
        tour.Start();
        tour.Next();
        tour.CurrentStepIndex.ShouldBe(1);
        tour.Next();
        tour.IsClosing.ShouldBeFalse();
        foreach (var result in new[] {
            new GdsExportResult { Success = false, Status = "Failed" },
            new GdsExportResult { Success = true, Status = "Script only" },
            new GdsExportResult { Success = false, GdsPath = "old.gds", Status = "Failed" } })
        {
            service.Result = result;
            await export.ExportScriptToGdsAsync("lesson.py");
            tour.IsClosing.ShouldBeFalse();
            tour.IsCompleted.ShouldBeFalse();
        }
        service.Result = new() { Success = true, GdsPath = "lesson.gds", Status = "Exported" };
        await export.ExportScriptToGdsAsync("lesson.py");
        tour.IsClosing.ShouldBeTrue();
        tour.Next();
        tour.IsCompleted.ShouldBeTrue();
        tour.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task RestartAndSkip_DoNotCreditEarlierExports()
    {
        var service = new ControlledExportService { Result = new() { Success = true, GdsPath = "lesson.gds" } };
        var export = new GdsExportViewModel(service, new global::CAP_Core.Export.PythonDiscoveryService());
        var tour = new ManufacturingTourViewModel(export);
        await export.ExportScriptToGdsAsync("lesson.py");
        tour.Start();
        tour.Next();
        tour.IsClosing.ShouldBeFalse();
        tour.Skip();
        await export.ExportScriptToGdsAsync("lesson.py");
        tour.IsCompleted.ShouldBeFalse();
        tour.Start();
        tour.Next();
        tour.IsClosing.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportStartedBeforeCurrentExportStage_DoesNotCompleteLesson(bool restart)
    {
        var service = new PendingExportService();
        var export = new GdsExportViewModel(service, new global::CAP_Core.Export.PythonDiscoveryService());
        var tour = new ManufacturingTourViewModel(export);
        tour.Start();
        if (restart) tour.Next();
        var pending = export.ExportScriptToGdsAsync("earlier.py");
        if (restart)
        {
            tour.Skip();
            tour.Start();
        }
        tour.Next();
        service.Complete();
        await pending;
        tour.IsClosing.ShouldBeFalse("an export begun before this export stage belongs to an earlier action");
        tour.IsCompleted.ShouldBeFalse();

        service.Reset();
        var fresh = export.ExportScriptToGdsAsync("current.py");
        service.Complete();
        await fresh;
        tour.IsClosing.ShouldBeTrue();
    }

    private sealed class PendingExportService : GdsExportService
    {
        private TaskCompletionSource<GdsExportResult> _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task<GdsExportResult> ExportToGdsAsync(string scriptPath, bool generateGds, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return ExportToGdsAsync(scriptPath, generateGds); }

        public override Task<GdsExportResult> ExportToGdsAsync(string scriptPath, bool generateGds) => _pending.Task;
        public void Complete() => _pending.SetResult(new() { Success = true, GdsPath = "lesson.gds", Status = "Exported" });
        public void Reset() => _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    [Fact]
    public async Task ExportRequestReservedBeforeLesson_CannotBeReissuedAfterSaveDialog()
    {
        var service = new ControlledExportService { Result = new() { Success = true, GdsPath = "lesson.gds" } };
        var export = new GdsExportViewModel(service, new global::CAP_Core.Export.PythonDiscoveryService());
        // The outer command reserves its identity before showing a save dialog.
        var earlierRequest = export.BeginExportRequest();
        var tour = new ManufacturingTourViewModel(export);
        tour.Start();
        tour.Next();
        await export.ExportScriptToGdsAsync("earlier.py", earlierRequest);
        export.LatestStartedGdsExportRequestId.ShouldBe(earlierRequest);
        tour.IsClosing.ShouldBeFalse();
        var currentRequest = export.BeginExportRequest();
        await export.ExportScriptToGdsAsync("current.py", currentRequest);
        tour.IsClosing.ShouldBeTrue();
    }
    private sealed class ControlledExportService : GdsExportService
    {
        public GdsExportResult Result { get; set; } = new();
        public override Task<GdsExportResult> ExportToGdsAsync(string scriptPath, bool generateGds, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return ExportToGdsAsync(scriptPath, generateGds); }

        public override Task<GdsExportResult> ExportToGdsAsync(string scriptPath, bool generateGds) => Task.FromResult(Result);
    }
}
