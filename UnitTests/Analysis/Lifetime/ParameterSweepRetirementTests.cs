using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Lifetime;
using Moq;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class ParameterSweepRetirementTests
{
    [AvaloniaTheory]
    [InlineData("configure")]
    [InlineData("replace")]
    [InlineData("remove")]
    public async Task ChangedContext_RejectsTheCompletedOldSweep(string change)
    {
        await using var fixture = new ParameterSweepLifetimeFixture();
        var running = fixture.RunAsync();
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            if (change == "configure") fixture.Panel.ConfigureForComponent(null, fixture.Canvas);
            else if (change == "replace") fixture.Canvas.Document.ReplaceDocument();
            else fixture.Canvas.Components.Remove(fixture.Target);
        }
        finally { await fixture.Runner.Barrier.FinishAsync(running); }
        fixture.Panel.ResultText.ShouldBeEmpty();
        fixture.Panel.IsSweeping.ShouldBeFalse();
        fixture.Errors.Entries.ShouldBeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retirement_WaitsForActualCleanupAndRejectsLaterWork(bool applicationStop)
    {
        var application = new ApplicationWorkCoordinator();
        await using var fixture = new ParameterSweepLifetimeFixture(application);
        var running = fixture.RunAsync();
        var stopping = Task.CompletedTask;
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            stopping = applicationStop ? application.StopAsync() : fixture.Panel.StopAsync();
            await fixture.Runner.Barrier.AssertDrainingAsync(stopping);
        }
        finally { await fixture.Runner.Barrier.FinishAsync(running, stopping); }
        fixture.Panel.ResultText.ShouldBeEmpty();
        fixture.Panel.IsSweeping.ShouldBeFalse();
        fixture.Errors.Entries.ShouldBeEmpty();
        await fixture.RunAsync();
        fixture.Runner.Calls.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task LivePhysicsEditsAfterCapture_DoNotReachTheRunningSolver()
    {
        await using var fixture = new ParameterSweepLifetimeFixture();
        var running = fixture.RunAsync();
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            // Bypass normal document notification to test input isolation independently of stale-result guards.
            fixture.Target.Component.WaveLengthToSMatrixMap.Clear();
            fixture.Target.Component.GetSlider(0)!.Value = 0.91;
        }
        finally { await fixture.Runner.Barrier.FinishAsync(running); }
        fixture.Runner.Completed.ShouldNotBeNull();
        fixture.Runner.Completed.Count.ShouldBe(3);
        fixture.Runner.Completed.ShouldAllBe(point => point.OutputPowers.Values.Any(power => power > 0));
        fixture.Runner.Completed.ShouldAllBe(point => point.OutputPowers[fixture.DownstreamOutput] > 0,
            "the detached solve must propagate through the actual canonical connection");
        fixture.Target.Component.GetSlider(0)!.Value.ShouldBe(0.91);
        fixture.Errors.Entries.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task ReconfigureWhileCsvPickerIsOpen_DoesNotWriteTheStaleResult()
    {
        await using var fixture = new ParameterSweepLifetimeFixture();
        fixture.Runner.Barrier.Release.TrySetResult();
        await fixture.RunAsync();
        fixture.Panel.ResultText.ShouldNotBeEmpty();
        var selected = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogs = new Mock<IFileDialogService>();
        dialogs.Setup(dialog => dialog.ShowSaveFileDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(selected.Task);
        fixture.Panel.FileDialogService = dialogs.Object;
        var path = Path.Combine(Path.GetTempPath(), $"lunima-stale-sweep-{Guid.NewGuid():N}.csv");
        var exporting = fixture.Panel.ExportCsvCommand.ExecuteAsync(null);
        try
        {
            dialogs.Verify(dialog => dialog.ShowSaveFileDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            fixture.Panel.ConfigureForComponent(null, fixture.Canvas);
        }
        finally
        {
            selected.TrySetResult(path);
            await exporting.WaitAsync(TimeSpan.FromSeconds(5));
        }
        try { File.Exists(path).ShouldBeFalse(); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
