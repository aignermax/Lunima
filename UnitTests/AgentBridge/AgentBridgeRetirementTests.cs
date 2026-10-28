using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Hosting;
using Shouldly;

namespace UnitTests.AgentBridge;

public sealed class AgentBridgeRetirementTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApplyingEnabledSettings_CannotReopenApplicationRetirement(bool whileDraining)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var bridge = new BridgeLifecycleTestHarness(new LifecycleProbeTool(async () =>
        {
            entered.TrySetResult();
            await release.Task;
        }));
        var settings = Path.Combine(Path.GetTempPath(), "retired-bridge-" + Guid.NewGuid() + ".json");
        var preferences = new UserPreferencesService(settings);
        preferences.SetAgentBridgeEnabled(true);
        preferences.SetAgentBridgePort(bridge.Server.Port);
        var host = new AgentBridgeHost(preferences, bridge.Server);
        var request = bridge.CallAsync();
        try
        {
            await entered.Task.WaitAsync(BridgeLifecycleTestHarness.Guard);
            var retirement = host.RetireAsync();
            retirement.IsCompleted.ShouldBeFalse();
            var applying = whileDraining ? host.ApplySettingsAsync() : Task.CompletedTask;
            host.IsRunning.ShouldBeFalse();

            release.TrySetResult();
            await Task.WhenAll(retirement, applying).WaitAsync(BridgeLifecycleTestHarness.Guard);
            if (!whileDraining) await host.ApplySettingsAsync().WaitAsync(BridgeLifecycleTestHarness.Guard);

            host.IsRunning.ShouldBeFalse("application retirement is terminal even when enabled settings arrive late");
        }
        finally
        {
            release.TrySetResult();
            await BridgeLifecycleTestHarness.ObserveRequestAsync(request);
            await host.StopAsync().WaitAsync(BridgeLifecycleTestHarness.Guard);
            File.Delete(settings);
        }
    }
}
