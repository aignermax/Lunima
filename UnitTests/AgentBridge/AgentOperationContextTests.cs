using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Hosting;
using CAP.Avalonia.Services.AgentBridge.Tools;
using Shouldly;
using Xunit;

namespace UnitTests.AgentBridge;

/// <summary>Verifies cancellation, generation ownership, and shutdown admission ordering.</summary>
public class AgentOperationContextTests
{
    [Fact]
    public async Task StopAsync_CancelsCooperativeToolAndDrainsItsFinally()
    {
        var entered = NewSignal();
        var exited = false;
        var tool = new ContextLifecycleProbeTool(async context =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken); }
            finally { exited = true; }
        });
        await using var bridge = new BridgeLifecycleTestHarness(tool);
        var request = bridge.CallAsync();
        await entered.Task.WaitAsync(BridgeLifecycleTestHarness.Guard);

        await bridge.Server.StopAsync().WaitAsync(BridgeLifecycleTestHarness.Guard);

        exited.ShouldBeTrue();
        await BridgeLifecycleTestHarness.ObserveRequestAsync(request);
    }

    [Fact]
    public async Task RequestCancellation_ReachesTheExecutingTool()
    {
        var entered = NewSignal();
        var tool = new ContextLifecycleProbeTool(async context =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
        });
        await using var bridge = new BridgeLifecycleTestHarness(tool);
        using var cancellation = new CancellationTokenSource();
        var invocation = bridge.Catalog.InvokeAsync(tool.Name, AgentBridgeTestStack.Parse("{}"), cancellation.Token);
        await entered.Task.WaitAsync(BridgeLifecycleTestHarness.Guard);

        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => invocation.WaitAsync(BridgeLifecycleTestHarness.Guard));
    }

    [Fact]
    public async Task StopAsync_RevokesDelayedCommitBeforeWorkerFinishes()
    {
        var entered = NewSignal();
        var release = NewSignal();
        var mutations = new List<string>();
        Exception? rejected = null;
        var tool = new ContextLifecycleProbeTool(async context =>
        {
            entered.SetResult();
            await release.Task;
            try { context.Commit(() => mutations.Add("late mutation")); }
            catch (OperationCanceledException error) { rejected = error; }
        });
        await using var bridge = new BridgeLifecycleTestHarness(tool);
        var request = bridge.CallAsync();
        try
        {
            await entered.Task.WaitAsync(BridgeLifecycleTestHarness.Guard);
            var stopping = bridge.Server.StopAsync();
            release.SetResult();
            await stopping.WaitAsync(BridgeLifecycleTestHarness.Guard);

            mutations.ShouldBeEmpty();
            rejected.ShouldBeOfType<OperationCanceledException>();
        }
        finally
        {
            release.TrySetResult();
            await BridgeLifecycleTestHarness.ObserveRequestAsync(request);
        }
    }

    [Fact]
    public async Task Restart_DoesNotRestoreAnOldContextsCommitPermission()
    {
        AgentToolContext? oldContext = null;
        var mutations = new List<string>();
        var tool = new ContextLifecycleProbeTool(context =>
        {
            oldContext ??= context;
            context.Commit(() => mutations.Add("current"));
            return Task.CompletedTask;
        });
        await using var bridge = new BridgeLifecycleTestHarness(tool);
        await bridge.CallAsync();
        await bridge.Server.StopAsync();
        bridge.Server.Start(0);

        Should.Throw<OperationCanceledException>(() => oldContext!.Commit(() => mutations.Add("stale")));
        await bridge.CallAsync();

        mutations.ShouldBe(new[] { "current", "current" });
    }

    [Fact]
    public async Task ConcurrentStopsAndEnable_DrainOnceBeforeRestart()
    {
        var entered = NewSignal();
        var release = NewSignal();
        var tool = new LifecycleProbeTool(async () => { entered.SetResult(); await release.Task; });
        await using var bridge = new BridgeLifecycleTestHarness(tool);
        var settingsPath = Path.Combine(Path.GetTempPath(), $"bridge-lifecycle-{Guid.NewGuid():N}.json");
        var preferences = new UserPreferencesService(settingsPath);
        preferences.SetAgentBridgePort(bridge.Server.Port);
        preferences.SetAgentBridgeEnabled(true);
        var host = new AgentBridgeHost(preferences, bridge.Server);
        var request = bridge.CallAsync();
        try
        {
            await entered.Task.WaitAsync(BridgeLifecycleTestHarness.Guard);
            var firstStop = host.StopAsync();
            var secondStop = host.StopAsync();
            var restarting = host.ApplySettingsAsync();

            firstStop.ShouldBeSameAs(secondStop);
            restarting.IsCompleted.ShouldBeFalse();
            host.IsRunning.ShouldBeFalse();
            release.SetResult();
            await Task.WhenAll(firstStop, secondStop, restarting).WaitAsync(BridgeLifecycleTestHarness.Guard);
            host.IsRunning.ShouldBeTrue();
        }
        finally
        {
            release.TrySetResult();
            await BridgeLifecycleTestHarness.ObserveRequestAsync(request);
            await host.StopAsync().WaitAsync(BridgeLifecycleTestHarness.Guard);
            File.Delete(settingsPath);
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task ThrowingCancellationCallback_DoesNotPreventHostRestartAfterDrain()
    {
        var entered = NewSignal();
        var finished = false;
        var tool = new ContextLifecycleProbeTool(async context =>
        {
            using var registration = context.CancellationToken.Register(() =>
                throw new InvalidOperationException("Controlled cancellation callback failure"));
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken); }
            finally { finished = true; }
        });
        var diagnostics = new List<Exception>();
        var bridge = new BridgeLifecycleTestHarness(tool, reportCancellationError: diagnostics.Add);
        var settingsPath = Path.Combine(Path.GetTempPath(), $"bridge-restart-{Guid.NewGuid():N}.json");
        var preferences = new UserPreferencesService(settingsPath);
        preferences.SetAgentBridgePort(bridge.Server.Port);
        preferences.SetAgentBridgeEnabled(true);
        var host = new AgentBridgeHost(preferences, bridge.Server);
        var request = bridge.CallAsync();
        try
        {
            await entered.Task.WaitAsync(BridgeLifecycleTestHarness.Guard);
            try { await host.StopAsync().WaitAsync(BridgeLifecycleTestHarness.Guard); }
            catch (AggregateException) { /* A reported callback failure must not poison the next start. */ }
            finished.ShouldBeTrue("accepted work must drain despite cancellation callback errors");
            diagnostics.Count.ShouldBe(1);
            diagnostics[0].ToString().ShouldContain("Controlled cancellation callback failure");
            await BridgeLifecycleTestHarness.ObserveRequestAsync(request);

            await host.ApplySettingsAsync().WaitAsync(BridgeLifecycleTestHarness.Guard);

            host.IsRunning.ShouldBeTrue("a completed shutdown must allow a fresh server generation");
        }
        finally
        {
            try { await bridge.DisposeAsync(); }
            catch (AggregateException) { }
            await BridgeLifecycleTestHarness.ObserveRequestAsync(request);
            File.Delete(settingsPath);
        }
    }
}
