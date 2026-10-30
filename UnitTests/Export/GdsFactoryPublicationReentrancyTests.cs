using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Export;
using CAP_Core;
using Shouldly;

namespace UnitTests.Export;

public sealed class GdsFactoryPublicationReentrancyTests
{
    [AvaloniaFact]
    public async Task FailureLogObserverRetiresOwner_NoLaterStatusIsPublished()
    {
        await using var fixture = new ExportWorkflowLifetimeFixture();
        fixture.Runner.FailAsMissingPackage = true;
        fixture.Runner.Release.TrySetResult();
        var errors = new ErrorConsoleService();
        await using var vm = new GdsFactoryExportViewModel(fixture.Launcher.Object, fixture.Canvas, fixture.Runner, errors)
            { FileDialogService = new ExportWorkflowLifetimeFixture.Picker(fixture.ScriptPath) };
        Task? stopping = null;
        string? statusAtStop = null;
        errors.EntryAdded += (_, _) =>
        {
            if (stopping != null) return;
            stopping = vm.StopAsync();
            statusAtStop = vm.StatusText;
        };
        await vm.ExportCommand.ExecuteAsync(null).WaitAsync(ExportWorkflowLifetimeFixture.Guard);
        stopping.ShouldNotBeNull("the failure result must pass through DescribeResult's error log");
        await stopping!.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
        vm.StatusText.ShouldBe(statusAtStop);
    }
}
