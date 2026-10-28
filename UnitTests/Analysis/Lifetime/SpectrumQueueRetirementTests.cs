using Avalonia.Headless.XUnit;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class SpectrumQueueRetirementTests
{
    [AvaloniaFact]
    public async Task Retirement_DrainsActiveSweepAndRejectsQueuedSuccessor()
    {
        await using var fixture = new SweepLifetimeFixture(spectrum: true);
        var first = fixture.RunAsync();
        var queued = Task.CompletedTask;
        var stopping = Task.CompletedTask;
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(first);
            queued = fixture.RunAsync();
            stopping = fixture.StopAsync();
            stopping.IsCompleted.ShouldBeFalse();
        }
        finally { await fixture.Runner.Barrier.FinishAsync(first, queued, stopping); }
        fixture.Runner.Calls.ShouldBe(1, "the queued sweep must not enter its worker after retirement");
        fixture.HasResult.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Retirement_CancelsPendingDebounceWithoutStartingSweep()
    {
        await using var fixture = new SweepLifetimeFixture(spectrum: true);
        var panel = fixture.Spectrum!;
        fixture.Runner.Barrier.Release.TrySetResult();
        panel.AutoRefreshDelay = TimeSpan.FromSeconds(30);
        panel.HasResult = true;
        panel.StartNm = 1400;
        var pending = panel.PendingAutoRefresh!;
        try
        {
            await fixture.StopAsync();
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
            fixture.Runner.Calls.ShouldBe(0);
        }
        finally
        {
            // On the old baseline, cancel the long timer through its existing
            // supersession path so a failing test leaves no background producer.
            panel.AutoRefreshDelay = TimeSpan.Zero;
            panel.StartNm = 1401;
            if (panel.PendingAutoRefresh != null)
                await panel.PendingAutoRefresh.WaitAsync(TimeSpan.FromSeconds(5));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
