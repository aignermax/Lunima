using CAP.Avalonia.Services.AgentBridge.Contracts;
using CAP.Avalonia.Services.AgentBridge.Hosting;
using CAP.Avalonia.Services.AgentBridge.Registration;
using Microsoft.Extensions.DependencyInjection;
using UnitTests.Helpers;
using CAP.Avalonia.ViewModels.Settings.AgentBridge;
using Shouldly;

namespace UnitTests.Architecture;

public sealed class AgentBridgePresentationBoundaryTests
{
    [Fact]
    public async Task SettingsCapabilities_ReuseTheConfiguredTransportAndRegistrationOwners()
    {
        await using var services = ProductionContainerTestHelper.BuildWithTempPreferences(
            Path.Combine(Path.GetTempPath(), $"lunima-bridge-di-{Guid.NewGuid():N}.json"));
        services.GetRequiredService<IAgentBridgeControl>().ShouldBeSameAs(services.GetRequiredService<AgentBridgeHost>());
        services.GetRequiredService<IAgentConnection>().ShouldBeSameAs(services.GetRequiredService<ClaudeCodeConnector>());
        services.GetRequiredService<AgentBridgeSettingsViewModel>().ShouldNotBeNull();
        services.GetRequiredService<ClaudeCodeConnectPromptViewModel>().ShouldNotBeNull();
    }

    [Theory]
    [InlineData(typeof(AgentBridgeSettingsViewModel))]
    [InlineData(typeof(ClaudeCodeConnectPromptViewModel))]
    public void SettingsAndPrompt_RequireCapabilitiesInsteadOfServerOrCliImplementations(Type viewModel)
    {
        var dependencies = viewModel.GetConstructors().SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType).ToArray();
        dependencies.Where(t => t.Namespace?.StartsWith("CAP.Avalonia.Services.AgentBridge") == true)
            .Where(t => !t.IsInterface).ShouldBeEmpty();
    }
}
