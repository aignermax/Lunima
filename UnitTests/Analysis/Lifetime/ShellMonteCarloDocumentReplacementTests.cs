using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using Moq;
using Shouldly;
using UnitTests.Analysis.Snapshots;
using UnitTests.Helpers;

namespace UnitTests.Analysis.Lifetime;

public sealed class ShellMonteCarloDocumentReplacementTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AcceptedReplacement_RevokesOldSamplesAndAdmitsFreshShellAnalysis(bool eye, bool load)
    {
        await using var fixture = new MonteCarloLifetimeFixture(eye: eye);
        await using var main = MainViewModelTestHelper.CreateMainViewModel(canvas: fixture.Canvas,
            monteCarloViewModel: fixture.Panel);
        var path = Path.Combine(Path.GetTempPath(), $"shell-monte-carlo-{Guid.NewGuid():N}.lun");
        await File.WriteAllTextAsync(path, "{\"FormatVersion\":\"2.0\",\"Components\":[],\"Connections\":[]}");
        var identity = fixture.Canvas.Document.CaptureIdentity();
        var running = fixture.RunAsync();
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            var accepted = load ? await main.FileOperations.LoadDesignFromPathAsync(path)
                : await main.FileOperations.TryNewProjectAsync();
            accepted.ShouldBeTrue();
            fixture.Canvas.Document.CaptureIdentity().ShouldNotBe(identity);
            fixture.Canvas.Components.ShouldBeEmpty();
            PopulateReplacement(fixture.Canvas);
            await fixture.Runner.Barrier.FinishAsync(running);
            fixture.Panel.HasResult.ShouldBeFalse("old samples cannot become a result for the replacement document");

            await fixture.RunAsync().WaitAsync(TimeSpan.FromSeconds(15));

            fixture.Runner.Calls.ShouldBe(2, "the persistent Monte Carlo panel must bind to the accepted replacement");
            fixture.Panel.HasResult.ShouldBeTrue();
            fixture.Panel.PlotModel.Series.ShouldNotBeEmpty();
            fixture.Errors.Entries.ShouldBeEmpty();
        }
        finally
        {
            await fixture.Runner.Barrier.FinishAsync(running);
            File.Delete(path);
        }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RejectedReplacement_KeepsTheAcceptedMonteCarloAnalysis(bool eye, bool load)
    {
        await using var fixture = new MonteCarloLifetimeFixture(eye: eye);
        await using var main = MainViewModelTestHelper.CreateMainViewModel(canvas: fixture.Canvas,
            monteCarloViewModel: fixture.Panel);
        var path = Path.Combine(Path.GetTempPath(), $"shell-monte-carlo-rejected-{Guid.NewGuid():N}.lun");
        await File.WriteAllTextAsync(path, "{\"FormatVersion\":\"2.0\",\"Components\":[],\"Connections\":[]}");
        var messages = new Mock<IMessageBoxService>();
        messages.Setup(service => service.ShowSavePromptAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(SavePromptResult.Cancel);
        main.FileOperations.MessageBoxService = messages.Object;
        main.FileOperations.HasUnsavedChanges = true;
        var identity = fixture.Canvas.Document.CaptureIdentity();
        var running = fixture.RunAsync();
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            var accepted = load ? await main.FileOperations.LoadDesignFromPathAsync(path)
                : await main.FileOperations.TryNewProjectAsync();
            accepted.ShouldBeFalse();
            fixture.Canvas.Document.CaptureIdentity().ShouldBe(identity);
            await fixture.Runner.Barrier.FinishAsync(running);

            fixture.Runner.Calls.ShouldBe(1);
            fixture.Panel.HasResult.ShouldBeTrue("a rejected New/Load request cannot revoke current samples");
            fixture.Errors.Entries.ShouldBeEmpty();
        }
        finally
        {
            await fixture.Runner.Barrier.FinishAsync(running);
            File.Delete(path);
        }
    }

    private static void PopulateReplacement(DesignCanvasViewModel canvas)
    {
        var replacement = new SweepSnapshotFixture();
        replacement.First.IsUserMarkedLightSource = true;
        replacement.First.LaserEnabled = true;
        replacement.Second.IsUserMarkedLightSource = true;
        replacement.Second.LaserEnabled = false;
        canvas.Components.Add(new ComponentViewModel(replacement.First));
        canvas.Components.Add(new ComponentViewModel(replacement.Second));
        canvas.RestoreConnectionAndViewModel(replacement.Wire);
    }
}
