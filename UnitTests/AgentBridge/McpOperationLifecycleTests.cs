using Shouldly;
using Xunit;

namespace UnitTests.AgentBridge;

/// <summary>Exercises accepted work through real HTTP dispatch and controlled async barriers.</summary>
public class McpOperationLifecycleTests
{
    [Fact]
    public async Task StopAsync_BeforeQueuedUiWorkRuns_PreventsToolExecution()
    {
        var dispatcher = new QueuedBridgeDispatcher();
        var mutations = 0;
        var tool = new LifecycleProbeTool(() => { mutations++; return Task.CompletedTask; });
        await using var bridge = new BridgeLifecycleTestHarness(tool, dispatcher);
        var request = bridge.CallAsync();
        try
        {
            await dispatcher.Queued.Task.WaitAsync(BridgeLifecycleTestHarness.Guard);
            var stopping = bridge.Server.StopAsync();
            await dispatcher.DrainAsync();
            await stopping.WaitAsync(BridgeLifecycleTestHarness.Guard);
            await BridgeLifecycleTestHarness.ObserveRequestAsync(request);

            mutations.ShouldBe(0, "a queued UI delegate must recheck admission after Stop");
        }
        finally
        {
            await dispatcher.DrainAsync();
            await BridgeLifecycleTestHarness.ObserveRequestAsync(request);
        }
    }

    [Fact]
    public async Task StopAsync_WithAcceptedTool_WaitsForItsCompletion()
    {
        var entered = NewSignal();
        var release = NewSignal();
        var finished = false;
        var tool = new LifecycleProbeTool(async () =>
        {
            entered.SetResult();
            await release.Task;
            finished = true;
        });
        await using var bridge = new BridgeLifecycleTestHarness(tool);
        var request = bridge.CallAsync();
        try
        {
            await entered.Task.WaitAsync(BridgeLifecycleTestHarness.Guard);
            var stopping = bridge.Server.StopAsync();
            await Should.ThrowAsync<TimeoutException>(() => stopping.WaitAsync(TimeSpan.FromMilliseconds(100)));
            release.TrySetResult();
            await stopping.WaitAsync(BridgeLifecycleTestHarness.Guard);

            finished.ShouldBeTrue("Stop must drain the accepted tool before returning");
        }
        finally
        {
            release.TrySetResult();
            await BridgeLifecycleTestHarness.ObserveRequestAsync(request);
        }
    }

    [Fact]
    public async Task ConcurrentToolCalls_DoNotInterleaveAcrossAwait()
    {
        var firstEntered = NewSignal();
        var releaseFirst = NewSignal();
        var secondEntered = NewSignal();
        var calls = 0;
        var tool = new LifecycleProbeTool(async () =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstEntered.SetResult();
                await releaseFirst.Task;
            }
            else secondEntered.SetResult();
        });
        await using var bridge = new BridgeLifecycleTestHarness(tool);
        var arguments = AgentBridgeTestStack.Parse("{}");
        var first = bridge.Catalog.InvokeAsync(tool.Name, arguments);
        Task? second = null;
        try
        {
            await firstEntered.Task.WaitAsync(BridgeLifecycleTestHarness.Guard);
            second = bridge.Catalog.InvokeAsync(tool.Name, arguments);
            secondEntered.Task.IsCompleted.ShouldBeFalse("another tool must not enter during an active edit");
            releaseFirst.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(BridgeLifecycleTestHarness.Guard);
            secondEntered.Task.IsCompletedSuccessfully.ShouldBeTrue();
        }
        finally
        {
            releaseFirst.TrySetResult();
            await first.WaitAsync(BridgeLifecycleTestHarness.Guard);
            if (second != null) await second.WaitAsync(BridgeLifecycleTestHarness.Guard);
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
