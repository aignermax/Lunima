using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services.Diagnostics;
using Shouldly;
using Xunit;

namespace UnitTests.Services.Diagnostics;

/// <summary>Checks the dispatcher after a command's first asynchronous yield.</summary>
public class UiThreadWatchdogContinuationTests
{
    [Fact]
    public async Task OperationEndsBeforeQueuedProbeRuns_ReportsOnlyOperationInterval()
    {
        Action? queued = null;
        var clock = new ManualClock();
        var heartbeat = new DispatcherHeartbeat(action => queued = action, clock);
        clock.Timestamp = 20;

        var completion = heartbeat.CompleteAsync();
        clock.Timestamp = 5000;
        queued.ShouldNotBeNull().Invoke();

        (await completion).ShouldBe(TimeSpan.FromMilliseconds(20));
    }

    private sealed class ManualClock : TimeProvider
    {
        internal long Timestamp { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Timestamp;
    }

    [Fact]
    public async Task DispatcherRejectsProbe_DiagnosticsDoNotFailTheCommand()
    {
        var heartbeat = new DispatcherHeartbeat(_ => throw new InvalidOperationException("dispatcher closed"));
        await Should.NotThrowAsync(() => heartbeat.CompleteAsync());
    }

    [Fact]
    public async Task DispatcherStopsPumping_HeartbeatCleanupStillCompletes()
    {
        Action? queued = null;
        var heartbeat = new DispatcherHeartbeat(action => queued = action);
        queued.ShouldNotBeNull();
        await heartbeat.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Should.NotThrow(() => queued!());
    }

    [AvaloniaFact]
    public async Task YieldThenBlock_ReportsTheContinuationStall()
    {
        var stalls = new List<UiStallMeasurement>();
        void Capture(UiStallMeasurement stall) => stalls.Add(stall);
        UiThreadWatchdog.StallDetected += Capture;
        try
        {
            await UiThreadWatchdog.MeasureAsync("late-block", async () =>
            {
                await Task.Delay(75);
                Dispatcher.UIThread.CheckAccess().ShouldBeTrue();
                Thread.Sleep(300);
            });
            stalls.ShouldHaveSingleItem().CommandName.ShouldBe("late-block");
            stalls[0].StallMs.ShouldBeGreaterThan(UiThreadWatchdog.BudgetMs);
        }
        finally { UiThreadWatchdog.StallDetected -= Capture; }
    }

    [AvaloniaFact]
    public async Task BackgroundDelay_DoesNotCountAsDispatcherStall()
    {
        var stalls = new List<UiStallMeasurement>();
        void Capture(UiStallMeasurement stall) => stalls.Add(stall);
        UiThreadWatchdog.StallDetected += Capture;
        try
        {
            await UiThreadWatchdog.MeasureAsync("background", () => Task.Delay(350));
            stalls.ShouldBeEmpty();
        }
        finally { UiThreadWatchdog.StallDetected -= Capture; }
    }

    [AvaloniaFact]
    public async Task YieldThenBlockAndThrow_StillReportsAndPreservesException()
    {
        var stalls = new List<UiStallMeasurement>();
        void Capture(UiStallMeasurement stall) => stalls.Add(stall);
        UiThreadWatchdog.StallDetected += Capture;
        try
        {
            var error = await Should.ThrowAsync<InvalidOperationException>(() =>
                UiThreadWatchdog.MeasureAsync("late-failure", async () =>
                {
                    await Task.Delay(75);
                    Thread.Sleep(300);
                    throw new InvalidOperationException("original");
                }));
            error.Message.ShouldBe("original");
            stalls.ShouldHaveSingleItem().CommandName.ShouldBe("late-failure");
        }
        finally { UiThreadWatchdog.StallDetected -= Capture; }
    }
}
