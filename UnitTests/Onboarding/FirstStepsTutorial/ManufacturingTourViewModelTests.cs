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
        var export = new GdsExportViewModel(service);
        var tour = new ManufacturingTourViewModel(export);
        tour.Start();
        tour.Next();
        tour.CurrentStepIndex.ShouldBe(1);
        tour.Next();
        tour.IsClosing.ShouldBeFalse();
        foreach (var result in new[] {
            new GdsExportService.ExportResult { Success = false, Status = "Failed" },
            new GdsExportService.ExportResult { Success = true, Status = "Script only" },
            new GdsExportService.ExportResult { Success = false, GdsPath = "old.gds", Status = "Failed" } })
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
        var export = new GdsExportViewModel(service);
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
        var export = new GdsExportViewModel(service);
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
        private TaskCompletionSource<ExportResult> _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task<ExportResult> ExportToGdsAsync(string scriptPath, bool generateGds) => _pending.Task;
        public void Complete() => _pending.SetResult(new() { Success = true, GdsPath = "lesson.gds", Status = "Exported" });
        public void Reset() => _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class ControlledExportService : GdsExportService
    {
        public ExportResult Result { get; set; } = new();
        public override Task<ExportResult> ExportToGdsAsync(string scriptPath, bool generateGds) => Task.FromResult(Result);
    }
}
