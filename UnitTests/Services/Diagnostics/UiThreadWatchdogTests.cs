using CAP.Avalonia.Services.Diagnostics;
using Shouldly;
using Xunit;

namespace UnitTests.Services.Diagnostics;

/// <summary>
/// Unit tests for <see cref="UiThreadWatchdog"/> (issue #1150): the harness relies on
/// these guarantees — under-budget work never fires, over-budget work fires exactly
/// once with the command name and stall duration attached, and subscriber failures
/// cannot propagate back into the measured command.
/// </summary>
public class UiThreadWatchdogTests
{
    [Fact]
    public void Measure_UnderBudget_DoesNotFireStallEvent()
    {
        using var _ = StallCapture.Start(out var stalls);
        UiThreadWatchdog.Measure("fast", () => { /* no work */ });
        stalls.ShouldBeEmpty();
    }

    [Fact]
    public void Measure_OverBudget_FiresOnceWithCommandNameAndDuration()
    {
        using var _ = StallCapture.Start(out var stalls);
        UiThreadWatchdog.Measure("slow", () => Thread.Sleep((int)UiThreadWatchdog.BudgetMs + 100));
        stalls.Count.ShouldBe(1);
        stalls[0].CommandName.ShouldBe("slow");
        stalls[0].StallMs.ShouldBeGreaterThan(UiThreadWatchdog.BudgetMs);
    }

    [Fact]
    public async Task MeasureAsync_TaskYieldsWithinBudget_DoesNotFire()
    {
        using var _ = StallCapture.Start(out var stalls);
        await UiThreadWatchdog.MeasureAsync("yielding", async () =>
        {
            // Yield control immediately so the UI-blocking prefix is ~0 ms; the long
            // remainder must NOT count against the budget.
            await Task.Delay((int)UiThreadWatchdog.BudgetMs + 200);
        });
        stalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task MeasureAsync_SynchronousTaskOverBudget_FiresOnce()
    {
        using var _ = StallCapture.Start(out var stalls);
        await UiThreadWatchdog.MeasureAsync("sync-heavy", () =>
        {
            Thread.Sleep((int)UiThreadWatchdog.BudgetMs + 100);
            return Task.CompletedTask;
        });
        stalls.Count.ShouldBe(1);
        stalls[0].CommandName.ShouldBe("sync-heavy");
    }

    [Fact]
    public void Measure_ThrowingBody_StillReportsStall()
    {
        using var _ = StallCapture.Start(out var stalls);
        Should.Throw<InvalidOperationException>(() =>
            UiThreadWatchdog.Measure("throwing", () =>
            {
                Thread.Sleep((int)UiThreadWatchdog.BudgetMs + 50);
                throw new InvalidOperationException("boom");
            }));
        stalls.Count.ShouldBe(1);
        stalls[0].CommandName.ShouldBe("throwing");
    }

    [Fact]
    public void Measure_FaultySubscriber_DoesNotPropagateException()
    {
        using var capture = StallCapture.Start(out _);
        UiThreadWatchdog.StallDetected += BadHandler;
        try
        {
            Should.NotThrow(() =>
                UiThreadWatchdog.Measure("guarded", () => Thread.Sleep((int)UiThreadWatchdog.BudgetMs + 50)));
        }
        finally
        {
            UiThreadWatchdog.StallDetected -= BadHandler;
        }

        static void BadHandler(UiStallMeasurement _) => throw new InvalidOperationException("subscriber bug");
    }

    [Fact]
    public void UiStallMeasurement_ToString_ContainsNameAndDuration()
    {
        new UiStallMeasurement("MyCommand", 250).ToString().ShouldBe("MyCommand → 250 ms");
    }

    /// <summary>
    /// Scoped <see cref="UiThreadWatchdog.StallDetected"/> subscription — disposing
    /// detaches so a test's buffer cannot leak into the next measurement.
    /// </summary>
    private sealed class StallCapture : IDisposable
    {
        private readonly List<UiStallMeasurement> _stalls;

        private StallCapture(List<UiStallMeasurement> stalls) => _stalls = stalls;

        public static StallCapture Start(out List<UiStallMeasurement> stalls)
        {
            stalls = new List<UiStallMeasurement>();
            var capture = new StallCapture(stalls);
            UiThreadWatchdog.StallDetected += capture.OnStall;
            return capture;
        }

        private void OnStall(UiStallMeasurement m) => _stalls.Add(m);

        public void Dispose() => UiThreadWatchdog.StallDetected -= OnStall;
    }
}
