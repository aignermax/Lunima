using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Views.Dialogs;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class OnaWindowRetirementTests
{
    [AvaloniaFact]
    public async Task ApprovedClose_AwaitsActualSweepCleanup()
    {
        await using var fixture = new SweepLifetimeFixture(spectrum: false);
        var window = new OnaAnalyzerWindow { DataContext = fixture.Ona };
        window.Show();
        var running = fixture.RunAsync();
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            window.Close();
            window.IsVisible.ShouldBeTrue("the window still owns unfinished sweep cleanup");
            await fixture.Runner.Barrier.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await fixture.Runner.Barrier.FinishAsync(running);
            await fixture.StopAsync();
            Dispatcher.UIThread.RunJobs();
            window.Close();
        }
        window.IsVisible.ShouldBeFalse();
        fixture.HasResult.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task RejectedClose_LeavesSweepUsable()
    {
        await using var fixture = new SweepLifetimeFixture(spectrum: false);
        var window = new OnaAnalyzerWindow { DataContext = fixture.Ona };
        EventHandler<WindowClosingEventArgs> reject = (_, args) => args.Cancel = true;
        window.Closing += reject;
        window.Show();
        var running = fixture.RunAsync();
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            window.Close();
            window.IsVisible.ShouldBeTrue();
            fixture.Runner.Barrier.Cancelled.Task.IsCompleted.ShouldBeFalse();
            await fixture.Runner.Barrier.FinishAsync(running);
            fixture.HasResult.ShouldBeTrue();
        }
        finally
        {
            await fixture.Runner.Barrier.FinishAsync(running);
            window.Closing -= reject;
            window.Close();
            await fixture.StopAsync();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
