using CAP.Avalonia.ViewModels.Simulation;
using Shouldly;

namespace UnitTests.Simulation.LiveSimulation;

public sealed class LiveSimulationRetirementTests
{
    // CancelAsync delivers token callbacks on the thread pool; a loaded CI runner can take
    // well over 250 ms to schedule them, so the success path gets a hang-guard deadline only.
    private static readonly TimeSpan CancellationDeadline = TimeSpan.FromSeconds(5);

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
            await Task.WhenAny(cancelled.Task, Task.Delay(CancellationDeadline));
            cancelled.Task.IsCompleted.ShouldBeTrue();
            stopping.IsCompleted.ShouldBeFalse();
        }
        finally { release.TrySetResult(); await scheduler.WhenSettledAsync(); }
        await stopping;
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
