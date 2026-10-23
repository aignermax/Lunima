using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services;
using Moq;
using Shouldly;

namespace UnitTests.UI.Flows;

public sealed class MainWindowRetirementTests
{
    [AvaloniaFact]
    public async Task CancelledClose_KeepsApplicationAndBindingsActive()
    {
        var retirements = 0;
        using var host = new UiFlowTestHost(retireApplicationAsync: () => { retirements++; return Task.CompletedTask; });
        var prompt = new Mock<IMessageBoxService>();
        prompt.Setup(service => service.ShowSavePromptAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(SavePromptResult.Cancel);
        host.Vm.FileOperations.MessageBoxService = prompt.Object;
        host.Vm.FileOperations.HasUnsavedChanges = true;

        host.Window.Close();
        Dispatcher.UIThread.RunJobs();

        retirements.ShouldBe(0);
        host.Window.IsVisible.ShouldBeTrue();
        host.Window.IsEnabled.ShouldBeTrue();
        host.Vm.LeftPanel.ShowNewComponentWindowAsync.ShouldNotBeNull();
        host.Vm.Canvas.SimulationRequested.ShouldNotBeNull();
        host.Vm.FileOperations.HasUnsavedChanges = false;
        host.Window.Close();
        Dispatcher.UIThread.RunJobs();
        await host.Vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task ApprovedClose_ReleasesWindowBindingsThenWaitsForRetirement()
    {
        var release = Signal();
        var closed = Signal();
        using var host = new UiFlowTestHost(retireApplicationAsync: () => release.Task);
        host.Window.Closed += (_, _) => closed.TrySetResult();
        host.Vm.FileOperations.HasUnsavedChanges = false;
        host.Window.Close();
        try
        {
            host.Window.IsVisible.ShouldBeTrue();
            host.Window.IsEnabled.ShouldBeFalse();
            host.Vm.LeftPanel.ShowNewComponentWindowAsync.ShouldBeNull();
            host.Vm.RightPanel.Netlist.CopyToClipboard.ShouldBeNull();
            host.Vm.FileOperations.FileDialogService.ShouldBeNull();
            host.Vm.PhotonTorchExportFormat.ShowOptionsDialogAsync.ShouldBeNull();
        }
        finally { release.TrySetResult(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(5)); await host.Vm.DisposeAsync(); }
        host.Window.IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task UpdaterRetirement_UsesApplicationOwnerAndKeepsFinalCloseAction()
    {
        var retirements = 0;
        using var host = new UiFlowTestHost(retireApplicationAsync: () => { retirements++; return Task.CompletedTask; });
        await host.Vm.Update.RetireBeforeShutdownAsync!();
        retirements.ShouldBe(1);
        host.Vm.Update.ShutdownAfterConfirmation.ShouldNotBeNull();
        host.Vm.Update.ShutdownAfterConfirmation!();
        Dispatcher.UIThread.RunJobs();
        host.Window.IsVisible.ShouldBeFalse();
        await host.Vm.DisposeAsync();
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
