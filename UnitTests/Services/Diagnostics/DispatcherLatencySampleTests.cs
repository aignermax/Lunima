using CAP.Avalonia.Services.Diagnostics;
using Shouldly;
using Xunit;

namespace UnitTests.Services.Diagnostics;

public class DispatcherLatencySampleTests
{
    [Fact]
    public void OperationCompletes_FinalSampleDoesNotIncludeDelayedCancellationContinuation()
    {
        var clock = new ManualClock();
        var sample = new DispatcherLatencySample(clock);
        clock.Timestamp = 20;
        sample.Complete();

        clock.Timestamp = 5000;

        sample.Elapsed.ShouldBe(TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public void DispatcherAcknowledges_LaterOperationCompletionCannotExtendLatency()
    {
        var clock = new ManualClock();
        var sample = new DispatcherLatencySample(clock);
        clock.Timestamp = 30;
        sample.Complete();
        clock.Timestamp = 300;
        sample.Complete();

        sample.Elapsed.ShouldBe(TimeSpan.FromMilliseconds(30));
    }

    [Fact]
    public void OutstandingProbe_PreservesRealLatencyUntilOperationCompletes()
    {
        var clock = new ManualClock();
        var sample = new DispatcherLatencySample(clock);
        clock.Timestamp = 300;
        sample.Elapsed.ShouldBe(TimeSpan.FromMilliseconds(300));
        sample.Complete();
        sample.Elapsed.ShouldBe(TimeSpan.FromMilliseconds(300));
    }

    private sealed class ManualClock : TimeProvider
    {
        internal long Timestamp { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Timestamp;
    }
}
