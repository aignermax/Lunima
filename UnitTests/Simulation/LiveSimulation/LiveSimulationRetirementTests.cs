using CAP.Avalonia.ViewModels.Simulation;
using Shouldly;

namespace UnitTests.Simulation.LiveSimulation;

public sealed class LiveSimulationRetirementTests
{
    [Fact]
    public async Task Stop_RejectsSubsequentRequests()
    {
        var runs = 0;
        var scheduler = new LiveSimulationScheduler(() => true, () => false,
            _ => { runs++; return Task.CompletedTask; }, TimeSpan.Zero);

        await scheduler.StopAsync();
        scheduler.Request();
        await scheduler.WhenSettledAsync();

        runs.ShouldBe(0);
    }

    [Fact]
    public async Task Stop_CancelsRunningWorkAndAwaitsItsCleanup()
    {
        var started = Signal();
        var cancelled = Signal();
        var release = Signal();
        var scheduler = new LiveSimulationScheduler(() => true, () => false, async token =>
        {
            using var registration = token.Register(() => cancelled.TrySetResult());
            started.TrySetResult();
            await release.Task;
        }, TimeSpan.Zero);
        scheduler.Request();
        await started.Task;

        var stopping = scheduler.StopAsync();
        try
        {
            await Task.WhenAny(cancelled.Task, Task.Delay(250));
            cancelled.Task.IsCompleted.ShouldBeTrue();
            stopping.IsCompleted.ShouldBeFalse();
        }
        finally { release.TrySetResult(); await scheduler.WhenSettledAsync(); }
        await stopping;
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
