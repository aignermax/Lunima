using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Export;
using CAP_Core.Export.Contracts;
using Moq;
using Shouldly;

namespace UnitTests.Export;

public sealed class GdsSettingsWorkflowLifetimeTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CallerCancellation_ReachesDiscoveryOrProbeAndWaitsForActualCompletion(bool discoveryCall)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var discovery = new Mock<IPythonDiscovery>();
        var backend = new Mock<IGdsExportSession>();
        async Task HoldAsync(CancellationToken token)
        {
            using var registration = token.Register(() => canceled.TrySetResult());
            started.TrySetResult();
            await release.Task;
        }
        discovery.Setup(value => value.DiscoverPythonWithNazcaAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken token) => { await HoldAsync(token); return new List<PythonInstallation>(); });
        backend.Setup(value => value.CheckPythonEnvironmentAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken token) => { await HoldAsync(token); return new PythonEnvironmentInfo(); });
        await using var vm = new GdsExportViewModel(backend.Object, discovery.Object);
        using var caller = new CancellationTokenSource();
        var running = discoveryCall ? vm.SearchForPythonAsync(caller.Token) : vm.CheckEnvironmentAsync(caller.Token);
        try
        {
            await started.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            caller.Cancel();
            await canceled.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            running.IsCompleted.ShouldBeFalse("forwarding cancellation cannot abandon actual backend cleanup");
            release.TrySetResult();
            await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(ExportWorkflowLifetimeFixture.Guard));
            vm.AvailablePythons.ShouldBeEmpty();
            vm.IsEnvironmentReady.ShouldBeFalse();
        }
        finally { release.TrySetResult(); await ExportWorkflowLifetimeFixture.FinishAsync(running); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SettingsRefresh_StopCancelsAndDrainsDiscoveryOrProbeWithoutLateSelection(bool holdDiscovery)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var discovery = new Mock<IPythonDiscovery>();
        var backend = new Mock<IGdsExportSession>();
        async Task HoldAsync(CancellationToken token)
        {
            using var registration = token.Register(() => canceled.TrySetResult());
            started.TrySetResult();
            await release.Task;
        }
        discovery.Setup(value => value.DiscoverPythonWithNazcaAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken token) =>
            {
                if (holdDiscovery) await HoldAsync(token);
                return holdDiscovery
                    ? [new PythonInstallation { Path = "late-python", Source = "controlled", PythonVersion = "3.12", NazcaVersion = "0.6" }]
                    : new List<PythonInstallation>();
            });
        backend.Setup(value => value.CheckPythonEnvironmentAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken token) =>
            {
                if (!holdDiscovery) await HoldAsync(token);
                return new PythonEnvironmentInfo { PythonAvailable = true, NazcaAvailable = true, PythonVersion = "late version" };
            });
        var persisted = new List<string?>();
        await using var vm = new GdsExportViewModel(backend.Object, discovery.Object)
            { OnPythonPathChanged = persisted.Add };
        var running = vm.RefreshInterpretersCommand.ExecuteAsync(null);
        try
        {
            await started.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            var stopping = vm.StopAsync();
            stopping.IsCompleted.ShouldBeFalse("settings activation still owns the held backend operation");
            await canceled.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            var state = (vm.CustomPythonPath, vm.PythonStatus, vm.NazcaStatus, vm.PythonAvailable, vm.NazcaAvailable);
            release.TrySetResult();
            await stopping.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            await ExportWorkflowLifetimeFixture.FinishAsync(running);
            (vm.CustomPythonPath, vm.PythonStatus, vm.NazcaStatus, vm.PythonAvailable, vm.NazcaAvailable).ShouldBe(state);
            persisted.ShouldBeEmpty();
            vm.AvailablePythons.ShouldBeEmpty();
        }
        finally
        {
            release.TrySetResult();
            await ExportWorkflowLifetimeFixture.FinishAsync(running);
            await vm.StopAsync();
        }
    }
}
