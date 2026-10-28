using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Views.Dialogs;
using CAP_Core.Export.Contracts;
using Moq;
using Shouldly;

namespace UnitTests.Export;

public sealed class OpenEblWindowLifetimeTests
{
    [AvaloniaFact]
    public async Task ApprovedClose_DrainsTheRunAndAllowsTheSingletonToReopen()
    {
        await using var fixture = new ExportWorkflowLifetimeFixture();
        var vm = new OpenEblWorkflowLifetimeTests.TestableViewModel(fixture, Checker());
        var window = new OpenEblCheckDialog { DataContext = vm };
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        var running = vm.CheckAnywayCommand.ExecuteAsync(null);
        try
        {
            await fixture.Runner.Started.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            window.Close();
            window.IsVisible.ShouldBeTrue("accepted work still owns this dialog until cleanup finishes");
            await fixture.Runner.Canceled.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            fixture.Runner.Release.TrySetResult();
            await running.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            await closed.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            vm.CopyToClipboard.ShouldBeNull();
            vm.PrepareForOpen("reopened design");
            var reopened = new OpenEblCheckDialog { DataContext = vm };
            reopened.Show();
            try
            {
                await vm.CheckAnywayCommand.ExecuteAsync(null).WaitAsync(ExportWorkflowLifetimeFixture.Guard);
                fixture.Runner.Calls.ShouldBe(2);
                vm.HasResult.ShouldBeTrue();
            }
            finally { reopened.Close(); }
        }
        finally
        {
            fixture.Runner.Release.TrySetResult();
            await ExportWorkflowLifetimeFixture.FinishAsync(running);
            await vm.StopAsync();
            Dispatcher.UIThread.RunJobs();
            window.Close();
            RemoveArtifacts(fixture);
        }
    }

    [AvaloniaFact]
    public async Task RejectedClose_DoesNotCancelAcceptedExport()
    {
        await using var fixture = new ExportWorkflowLifetimeFixture();
        var vm = new OpenEblWorkflowLifetimeTests.TestableViewModel(fixture, Checker());
        var window = new OpenEblCheckDialog { DataContext = vm };
        EventHandler<WindowClosingEventArgs> reject = (_, args) => args.Cancel = true;
        window.Closing += reject;
        window.Show();
        var running = vm.CheckAnywayCommand.ExecuteAsync(null);
        try
        {
            await fixture.Runner.Started.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            window.Close();
            window.IsVisible.ShouldBeTrue();
            fixture.Runner.Token.IsCancellationRequested.ShouldBeFalse();
            fixture.Runner.Release.TrySetResult();
            await running.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            vm.HasResult.ShouldBeTrue();
        }
        finally
        {
            fixture.Runner.Release.TrySetResult();
            await ExportWorkflowLifetimeFixture.FinishAsync(running);
            window.Closing -= reject;
            window.Close();
            await vm.StopAsync();
            Dispatcher.UIThread.RunJobs();
            RemoveArtifacts(fixture);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedWindow_ReleasesOnlyItsOwnClipboardCallback(bool replaced)
    {
        await using var fixture = new ExportWorkflowLifetimeFixture();
        var vm = new OpenEblWorkflowLifetimeTests.TestableViewModel(fixture, Checker());
        var window = new OpenEblCheckDialog { DataContext = vm };
        window.Show();
        vm.CopyToClipboard.ShouldNotBeNull();
        Func<string, Task> foreign = _ => Task.CompletedTask;
        if (replaced) vm.CopyToClipboard = foreign;
        window.Close();
        Dispatcher.UIThread.RunJobs();
        if (replaced) vm.CopyToClipboard.ShouldBeSameAs(foreign);
        else vm.CopyToClipboard.ShouldBeNull();
        await vm.StopAsync();
    }

    internal static IOpenEblSubmissionChecker Checker()
    {
        var checker = new Mock<IOpenEblSubmissionChecker>();
        checker.Setup(value => value.CheckAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenEblCheckReport
                { Status = OpenEblCheckStatus.Passed, SubmissionChecksPassed = true, VerificationPassed = true });
        return checker.Object;
    }

    internal static void RemoveArtifacts(ExportWorkflowLifetimeFixture fixture)
    {
        if (fixture.Runner.LastScript is not { } script) return;
        if (File.Exists(script)) File.Delete(script);
        var artifact = Path.ChangeExtension(script, ".gds");
        if (File.Exists(artifact)) File.Delete(artifact);
    }
}
