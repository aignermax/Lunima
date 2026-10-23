using Shouldly;
using Xunit;

namespace UnitTests.AgentBridge;

/// <summary>Protects the caller of Stop from synchronous work in cancellation registrations.</summary>
public class McpCancellationCallbackTests
{
    [Fact]
    public async Task StopAsync_BlockingToolCancellationCallback_DoesNotBlockCallerHeartbeat()
    {
        var entered = NewSignal();
        var callbackEntered = NewSignal();
        var heartbeat = NewSignal();
        using var releaseCallback = new ManualResetEventSlim();
        var tool = new ContextLifecycleProbeTool(async context =>
        {
            using var registration = context.CancellationToken.Register(() =>
            {
                callbackEntered.TrySetResult();
                releaseCallback.Wait(BridgeLifecycleTestHarness.Guard);
            });
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
        });
        await using var bridge = new BridgeLifecycleTestHarness(tool);
        var request = bridge.CallAsync();
        Task? stopping = null;
        try
        {
            await entered.Task.WaitAsync(BridgeLifecycleTestHarness.Guard);
            stopping = Task.Factory.StartNew(async () =>
            {
                var drain = bridge.Server.StopAsync();
                heartbeat.SetResult();
                await drain;
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
            await callbackEntered.Task.WaitAsync(BridgeLifecycleTestHarness.Guard);

            await heartbeat.Task.WaitAsync(TimeSpan.FromMilliseconds(500));

            releaseCallback.IsSet.ShouldBeFalse("Stop must yield before the callback is released");
        }
        finally
        {
            releaseCallback.Set();
            if (stopping != null) await stopping.WaitAsync(BridgeLifecycleTestHarness.Guard);
            await BridgeLifecycleTestHarness.ObserveRequestAsync(request);
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
