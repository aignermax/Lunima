using CAP.Avalonia.ViewModels.Simulation;
using Shouldly;
using Xunit;

namespace UnitTests.Simulation.LiveSimulation;

/// <summary>
/// The live-simulation scheduler: debounced, cancel-the-stale-run, never overlapping, and
/// deferred while routing is in flight.
/// </summary>
public class LiveSimulationSchedulerTests
{
    private static readonly TimeSpan ShortDebounce = TimeSpan.FromMilliseconds(30);

    private bool _isOn = true;
    private bool _isRouting;
    private readonly List<CancellationToken> _runTokens = new();
    private int _concurrentRuns;
    private int _maxConcurrentRuns;
    private TimeSpan _runDuration = TimeSpan.Zero;

    private LiveSimulationScheduler CreateScheduler() =>
        new(() => _isOn, () => _isRouting, SimulateAsync, ShortDebounce);

    private async Task SimulateAsync(CancellationToken token)
    {
        lock (_runTokens)
            _runTokens.Add(token);
        var running = Interlocked.Increment(ref _concurrentRuns);
        _maxConcurrentRuns = Math.Max(_maxConcurrentRuns, running);
        try
        {
            if (_runDuration > TimeSpan.Zero)
                await Task.Delay(_runDuration, token);
        }
        finally
        {
            Interlocked.Decrement(ref _concurrentRuns);
        }
    }

    [Fact]
    public async Task Request_WhileSimulationIsOff_RunsNothing()
    {
        _isOn = false;
        var scheduler = CreateScheduler();

        scheduler.Request();
        await scheduler.WhenSettledAsync();

        scheduler.RunCount.ShouldBe(0);
    }

    [Fact]
    public async Task TwoRapidRequests_RunTheSimulationOnce()
    {
        var scheduler = CreateScheduler();

        scheduler.Request();
        scheduler.Request();
        await scheduler.WhenSettledAsync();

        scheduler.RunCount.ShouldBe(1, "edits inside the debounce window coalesce into one run");
    }

    [Fact]
    public async Task RequestDuringARun_CancelsTheStaleRun_AndRunsAgainWithoutOverlap()
    {
        _runDuration = TimeSpan.FromMilliseconds(400);
        var scheduler = CreateScheduler();

        scheduler.Request();
        await WaitUntilAsync(() => scheduler.RunCount == 1);
        scheduler.Request();
        await scheduler.WhenSettledAsync();

        scheduler.RunCount.ShouldBe(2);
        _runTokens[0].IsCancellationRequested.ShouldBeTrue("the superseded run must not apply its result");
        _runTokens[1].IsCancellationRequested.ShouldBeFalse("the latest run is the one that counts");
        _maxConcurrentRuns.ShouldBe(1, "a new run waits for the cancelled one to finish");
    }

    [Fact]
    public async Task RequestWhileRouting_WaitsForTheRoutingPass()
    {
        _isRouting = true;
        var scheduler = CreateScheduler();

        scheduler.Request();
        await scheduler.WhenSettledAsync();

        scheduler.RunCount.ShouldBe(0, "wire geometry is still changing while routing runs");
        scheduler.IsWaitingForRouting.ShouldBeTrue();

        _isRouting = false;
        scheduler.OnRoutingFinished();
        await scheduler.WhenSettledAsync();

        scheduler.RunCount.ShouldBe(1);
        scheduler.IsWaitingForRouting.ShouldBeFalse();
    }

    [Fact]
    public async Task Cancel_DropsAPendingRun()
    {
        var scheduler = CreateScheduler();

        scheduler.Request();
        scheduler.Cancel();
        await scheduler.WhenSettledAsync();

        scheduler.RunCount.ShouldBe(0);
    }

    [Fact]
    public async Task SwitchedOffDuringTheDebounce_RunsNothing()
    {
        var scheduler = CreateScheduler();

        scheduler.Request();
        _isOn = false;
        await scheduler.WhenSettledAsync();

        scheduler.RunCount.ShouldBe(0);
    }

    [Fact]
    public async Task FailedRun_DoesNotBreakLaterRuns()
    {
        var calls = 0;
        var scheduler = new LiveSimulationScheduler(() => true, () => false, _ =>
        {
            calls++;
            return calls == 1 ? Task.FromException(new InvalidOperationException("boom")) : Task.CompletedTask;
        }, ShortDebounce);

        scheduler.Request();
        await scheduler.WhenSettledAsync();
        scheduler.Request();
        await scheduler.WhenSettledAsync();

        calls.ShouldBe(2);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        condition().ShouldBeTrue();
    }
}
