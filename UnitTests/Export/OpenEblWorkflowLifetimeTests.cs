using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Export.OpenEbl;
using CAP_Core.Export.Contracts;
using Moq;
using Shouldly;

namespace UnitTests.Export;

public sealed class OpenEblWorkflowLifetimeTests
{
    [AvaloniaFact]
    public async Task UserCancelDuringExport_PropagatesTokenAndDoesNotStartChecker()
    {
        await using var fixture = new ExportWorkflowLifetimeFixture();
        var checker = new Mock<IOpenEblSubmissionChecker>();
        var vm = new TestableViewModel(fixture, checker.Object);
        var running = vm.CheckAnywayCommand.ExecuteAsync(null);
        try
        {
            await fixture.Runner.Started.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            vm.CancelCommand.Execute(null);
            await fixture.Runner.Canceled.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            running.IsCompleted.ShouldBeFalse("cancel requests cleanup but cannot abandon the runner");
            fixture.Runner.Release.TrySetResult();
            await running.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            checker.Verify(value => value.CheckAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            vm.GdsPathText.ShouldBeNullOrEmpty();
            vm.HasResult.ShouldBeFalse();
        }
        finally { await CleanupAsync(fixture, vm, running); }
    }

    [AvaloniaFact]
    public async Task StopDuringExport_DrainsRunnerAndRejectsLatePathPublication()
    {
        await using var fixture = new ExportWorkflowLifetimeFixture();
        var checker = new Mock<IOpenEblSubmissionChecker>();
        checker.Setup(value => value.CheckAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenEblCheckReport { Status = OpenEblCheckStatus.Passed, SubmissionChecksPassed = true, VerificationPassed = true });
        var vm = new TestableViewModel(fixture, checker.Object);
        var running = vm.CheckAnywayCommand.ExecuteAsync(null);
        try
        {
            await fixture.Runner.Started.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            var stopping = vm.StopAsync();
            stopping.IsCompleted.ShouldBeFalse();
            await fixture.Runner.Canceled.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            var status = vm.StatusText;
            fixture.Runner.Release.TrySetResult();
            await stopping.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            await running.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            vm.StatusText.ShouldBe(status);
            vm.HasResult.ShouldBeFalse();
            vm.GdsPathText.ShouldBeNullOrEmpty();
            checker.Verify(value => value.CheckAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally { await CleanupAsync(fixture, vm, running); }
    }

    [AvaloniaFact]
    public async Task StopDuringChecker_DrainsAcceptedCheckAndRejectsLateReport()
    {
        await using var fixture = new ExportWorkflowLifetimeFixture();
        fixture.Runner.Release.TrySetResult();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checker = new Mock<IOpenEblSubmissionChecker>();
        checker.Setup(value => value.CheckAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken token) =>
            {
                using var registration = token.Register(() => canceled.TrySetResult());
                started.TrySetResult();
                await release.Task;
                return new OpenEblCheckReport { Status = OpenEblCheckStatus.Passed, SubmissionChecksPassed = true, VerificationPassed = true };
            });
        var vm = new TestableViewModel(fixture, checker.Object);
        var running = vm.CheckAnywayCommand.ExecuteAsync(null);
        try
        {
            await started.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            var stopping = vm.StopAsync();
            stopping.IsCompleted.ShouldBeFalse();
            await canceled.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            var status = vm.StatusText;
            release.TrySetResult();
            await stopping.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            await running.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            vm.StatusText.ShouldBe(status);
            vm.HasResult.ShouldBeFalse();
        }
        finally { release.TrySetResult(); await CleanupAsync(fixture, vm, running); }
    }

    internal sealed class TestableViewModel : OpenEblCheckViewModel
    {
        internal TestableViewModel(ExportWorkflowLifetimeFixture fixture, IOpenEblSubmissionChecker checker)
            : base(fixture.Canvas, new SimpleNazcaExporter(), fixture.Runner, checker)
        { FileName = $"EBeam_lifetime_{Guid.NewGuid():N}"; }
        protected override string BuildNazcaScript() => "# controlled script";
    }

    private static async Task CleanupAsync(ExportWorkflowLifetimeFixture fixture, OpenEblCheckViewModel vm, Task running)
    {
        fixture.Runner.Release.TrySetResult();
        await ExportWorkflowLifetimeFixture.FinishAsync(running);
        await vm.StopAsync();
        if (fixture.Runner.LastScript is { } script)
        {
            if (File.Exists(script)) File.Delete(script);
            var artifact = Path.ChangeExtension(script, ".gds");
            if (File.Exists(artifact)) File.Delete(artifact);
        }
    }
}
