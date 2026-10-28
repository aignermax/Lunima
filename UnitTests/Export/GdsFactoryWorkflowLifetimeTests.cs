using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Export;
using Moq;
using Shouldly;

namespace UnitTests.Export;

public sealed class GdsFactoryWorkflowLifetimeTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopDuringScript_DrainsAcceptedWorkWithoutViewerOrNextScript(bool nazcaPartial)
    {
        await using var fixture = new ExportWorkflowLifetimeFixture(nazcaPartial);
        var vm = Create(fixture);
        var running = vm.ExportCommand.ExecuteAsync(null);
        try
        {
            await fixture.Runner.Started.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            fixture.Runner.Token.CanBeCanceled.ShouldBeTrue();
            var stopping = vm.StopAsync();
            stopping.IsCompleted.ShouldBeFalse();
            await fixture.Runner.Canceled.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            var status = vm.StatusText;
            fixture.Runner.Release.TrySetResult();
            await stopping.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            await running.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            fixture.Runner.Calls.ShouldBe(1);
            fixture.Launcher.Verify(value => value.OpenFileOrDirectory(It.IsAny<string>()), Times.Never);
            vm.StatusText.ShouldBe(status);
        }
        finally { await CleanupAsync(fixture, vm, running); }
    }

    [AvaloniaFact]
    public async Task StopDuringPicker_WaitsForDismissalAndDoesNotWriteOrLaunch()
    {
        await using var fixture = new ExportWorkflowLifetimeFixture();
        var picker = new ExportWorkflowLifetimeFixture.Picker(fixture.ScriptPath, hold: true);
        var vm = Create(fixture);
        vm.FileDialogService = picker;
        fixture.Runner.Release.TrySetResult();
        var running = vm.ExportCommand.ExecuteAsync(null);
        try
        {
            await picker.Started.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            var stopping = vm.StopAsync();
            stopping.IsCompleted.ShouldBeFalse("the accepted picker callback is still outstanding");
            picker.Release.TrySetResult();
            await stopping.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            await running.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            File.Exists(fixture.ScriptPath).ShouldBeFalse();
            fixture.Runner.Calls.ShouldBe(0);
        }
        finally { picker.Release.TrySetResult(); await CleanupAsync(fixture, vm, running); }
    }

    [AvaloniaFact]
    public async Task StopDuringInstall_CancelsAndDrainsInstallerWithoutRetryOrLateProgress()
    {
        await using var fixture = new ExportWorkflowLifetimeFixture();
        fixture.Runner.FailAsMissingPackage = true;
        fixture.Runner.Release.TrySetResult();
        var vm = Create(fixture);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IProgress<string>? progress = null;
        vm.EnsureGdsFactoryAsync = async (notifications, token) =>
        {
            progress = notifications;
            using var registration = token.Register(() => canceled.TrySetResult());
            started.TrySetResult();
            await release.Task;
            return true;
        };
        var running = vm.ExportCommand.ExecuteAsync(null);
        try
        {
            await started.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            var stopping = vm.StopAsync();
            stopping.IsCompleted.ShouldBeFalse();
            await canceled.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            var status = vm.StatusText;
            progress!.Report("obsolete installer progress");
            release.TrySetResult();
            await stopping.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            await running.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            fixture.Runner.Calls.ShouldBe(1);
            vm.StatusText.ShouldBe(status);
        }
        finally { release.TrySetResult(); await CleanupAsync(fixture, vm, running); }
    }

    private static GdsFactoryExportViewModel Create(ExportWorkflowLifetimeFixture fixture) =>
        new(fixture.Launcher.Object, fixture.Canvas, fixture.Runner)
            { FileDialogService = new ExportWorkflowLifetimeFixture.Picker(fixture.ScriptPath) };

    private static async Task CleanupAsync(ExportWorkflowLifetimeFixture fixture, GdsFactoryExportViewModel vm, Task running)
    {
        fixture.Runner.Release.TrySetResult();
        await ExportWorkflowLifetimeFixture.FinishAsync(running);
        await vm.StopAsync();
    }
}
