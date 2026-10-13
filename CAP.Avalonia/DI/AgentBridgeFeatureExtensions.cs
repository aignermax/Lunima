using System.Reflection;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Hosting;
using CAP.Avalonia.Services.AgentBridge.Protocol;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AgentBridge.Tools.Edit;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;
using CAP.Avalonia.Services.AgentBridge.Tools.Session;
using CAP.Avalonia.Services.AgentBridge.Transport;
using CAP.Avalonia.Services.AiTools;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Settings.AgentBridge;
using Microsoft.Extensions.DependencyInjection;

namespace CAP.Avalonia.DI;

/// <summary>
/// Registers the agent bridge: a loopback MCP server inside the running app that lets an
/// external AI agent (e.g. Claude Code) inspect and minimally edit the open design.
/// The tool list is the bridge's own tools plus every in-app assistant tool from the
/// shared <see cref="IAiToolRegistry"/>. The server stays off until the user opts in.
/// </summary>
internal static class AgentBridgeFeatureExtensions
{
    /// <summary>
    /// Adds the bridge's design session, tools, catalog, JSON-RPC dispatcher, HTTP server,
    /// lifecycle host and settings view-model. To add a bridge tool: one
    /// <c>AddTransient&lt;IAgentBridgeTool, YourTool&gt;()</c> line here.
    /// </summary>
    public static IServiceCollection AddAgentBridgeFeature(this IServiceCollection services)
    {
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton(sp => new AgentDesignSession(
            sp.GetRequiredService<DesignCanvasViewModel>(),
            sp.GetRequiredService<CommandManager>(),
            () => sp.GetService<MainViewModel>()?.FileOperations,
            sp.GetRequiredService<ExampleDesignsService>()));
        services.AddSingleton(sp => new CanvasSnapshotRenderer(() => sp.GetService<MainViewModel>()));

        services.AddTransient<IAgentBridgeTool, GetDesignSummaryTool>();
        services.AddTransient<IAgentBridgeTool, ListConnectionsTool>();
        services.AddTransient<IAgentBridgeTool, ListComponentsTool>();
        services.AddTransient<IAgentBridgeTool, GetRoutingTimingsTool>();
        services.AddTransient<IAgentBridgeTool, MoveComponentTool>();
        services.AddTransient<IAgentBridgeTool, RerouteConnectionsTool>();
        services.AddTransient<IAgentBridgeTool, DeleteComponentTool>();
        services.AddTransient<IAgentBridgeTool, DeleteConnectionTool>();
        services.AddTransient<IAgentBridgeTool, UndoTool>();
        services.AddTransient<IAgentBridgeTool, LoadExampleTool>();
        services.AddTransient<IAgentBridgeTool, ScreenshotTool>();
        services.AddSingleton<AgentToolCatalog>();

        services.AddSingleton(sp => new McpJsonRpcDispatcher(
            sp.GetRequiredService<AgentToolCatalog>(), AppVersion()));
        services.AddSingleton(sp =>
        {
            var preferences = sp.GetRequiredService<UserPreferencesService>();
            return new McpHttpEndpoint(
                sp.GetRequiredService<McpJsonRpcDispatcher>(), preferences.GetOrCreateAgentBridgeToken);
        });
        services.AddSingleton<McpHttpServer>();
        services.AddSingleton<AgentBridgeHost>();
        services.AddTransient<AgentBridgeSettingsViewModel>();

        return services;
    }

    private static string AppVersion() =>
        (Assembly.GetEntryAssembly() ?? typeof(AgentBridgeFeatureExtensions).Assembly)
            .GetName().Version?.ToString(3) ?? "0.0.0";
}
