using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Export;
using CAP_Core.Export.Contracts;
using Moq;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.Export;

public sealed class GdsExportWorkflowLifetimeTests
{
    [AvaloniaFact]
    public async Task ExportPassesAnOwnedCancellationTokenToRunner()
    {
        await using var fixture = new ExportWorkflowLifetimeFixture();
        var vm = Create(fixture);
        var running = vm.ExportScriptToGdsAsync(fixture.ScriptPath);
        try
        {
            await fixture.Runner.Started.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            fixture.Runner.Token.CanBeCanceled.ShouldBeTrue();
        }
        finally
        {
            fixture.Runner.Release.TrySetResult();
            await ExportWorkflowLifetimeFixture.FinishAsync(running);
            await vm.StopAsync();
        }
    }

    [AvaloniaFact]
    public async Task StopWaitsForAcceptedRunnerAndSuppressesLateSuccess()
    {
        await using var fixture = new ExportWorkflowLifetimeFixture();
        var vm = Create(fixture);
        var running = vm.ExportScriptToGdsAsync(fixture.ScriptPath);
        try
        {
            await fixture.Runner.Started.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            var stopping = vm.StopAsync();
            stopping.IsCompleted.ShouldBeFalse("the runner has not finished cleanup");
            await fixture.Runner.Canceled.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            var status = vm.LastExportStatus;
            fixture.Runner.Release.TrySetResult();
            await stopping.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            await ExportWorkflowLifetimeFixture.FinishAsync(running);
            vm.LastExportStatus.ShouldBe(status);
            vm.LastSuccessfulGdsExportRequestId.ShouldBe(0);
        }
        finally
        {
            fixture.Runner.Release.TrySetResult();
            await ExportWorkflowLifetimeFixture.FinishAsync(running);
            await vm.StopAsync();
        }
    }

    [AvaloniaFact]
    public async Task RetiredExportRejectsNewAdmission()
    {
        await using var fixture = new ExportWorkflowLifetimeFixture();
        var vm = Create(fixture);
        fixture.Runner.Release.TrySetResult();
        await vm.StopAsync();
        await ExportWorkflowLifetimeFixture.FinishAsync(vm.ExportScriptToGdsAsync(fixture.ScriptPath));
        fixture.Runner.Calls.ShouldBe(0);
        vm.LastSuccessfulGdsExportRequestId.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task ActualMainStopDrainsTheSharedExportViewModel()
    {
        await using var fixture = new ExportWorkflowLifetimeFixture();
        var vm = Create(fixture);
        var main = MainViewModelTestHelper.CreateMainViewModel(canvas: fixture.Canvas, gdsExportViewModel: vm);
        var running = vm.ExportScriptToGdsAsync(fixture.ScriptPath);
        try
        {
            await fixture.Runner.Started.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            var stopping = main.StopAsync();
            await Task.Yield();
            await fixture.Runner.Canceled.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            stopping.IsCompleted.ShouldBeFalse();
            fixture.Runner.Release.TrySetResult();
            await stopping.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            vm.LastSuccessfulGdsExportRequestId.ShouldBe(0);
        }
        finally
        {
            fixture.Runner.Release.TrySetResult();
            await ExportWorkflowLifetimeFixture.FinishAsync(running);
            await main.DisposeAsync();
        }
    }

    private static GdsExportViewModel Create(ExportWorkflowLifetimeFixture fixture) =>
        new(fixture.Runner, Mock.Of<IPythonDiscovery>());
}
