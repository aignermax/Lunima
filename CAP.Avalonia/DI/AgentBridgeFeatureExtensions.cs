using CAP.Avalonia.Services.AgentBridge.Contracts;
using System.Reflection;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Hosting;
using CAP.Avalonia.Services.AgentBridge.Protocol;
using CAP.Avalonia.Services.AgentBridge.Registration;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AgentBridge.Tools.Analysis;
using CAP.Avalonia.Services.AgentBridge.Tools.Author;
using CAP.Avalonia.Services.AgentBridge.Tools.Dies;
using CAP.Avalonia.Services.AgentBridge.Tools.Edit;
using CAP.Avalonia.Services.AgentBridge.Tools.Exchange;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;
using CAP.Avalonia.Services.AgentBridge.Tools.Layout;
using CAP.Avalonia.Services.AgentBridge.Tools.Logic;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Cut;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Geometry;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;
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
            sp.GetRequiredService<ExampleDesignsService>(),
            () => sp.GetService<MainViewModel>()?.LeftPanel.AllTemplates.ToList()
                  ?? (IReadOnlyList<CAP.Avalonia.ViewModels.Library.ComponentTemplate>)Array.Empty<CAP.Avalonia.ViewModels.Library.ComponentTemplate>(),
            () => sp.GetService<MainViewModel>() is { } main
                ? CAP_Core.Components.Process.ProcessCatalog.BuildGroups(main.LeftPanel.GetLoadedPdkProcessEntries())
                : Array.Empty<CAP_Core.Components.Process.ProcessGroup>(),
            () => sp.GetService<MainViewModel>()?.CanvasInteraction.PlacementContext
                  ?? CAP_Core.Components.Process.PlacementPolicyContext.Unrestricted,
            () => sp.GetService<MainViewModel>()));
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
        services.AddTransient<IAgentBridgeTool, SaveDesignTool>();
        services.AddTransient<IAgentBridgeTool, GetConnectionGeometryTool>();
        services.AddTransient<IAgentBridgeTool, ShiftSegmentTool>();
        services.AddTransient<IAgentBridgeTool, ListCutCandidatesTool>();
        services.AddTransient<IAgentBridgeTool, InsertCrossingTool>();
        services.AddTransient<IAgentBridgeTool, MoveCrossingAlongAxisTool>();
        services.AddTransient<IAgentBridgeTool, SuggestFixesTool>();
        services.AddTransient<IAgentBridgeTool, ApplySuggestionTool>();
        services.AddTransient<IAgentBridgeTool, ListDiesTool>();
        services.AddTransient<IAgentBridgeTool, CreateDieTool>();
        services.AddTransient<IAgentBridgeTool, SetDieProcessTool>();
        services.AddTransient<IAgentBridgeTool, MoveDieTool>();
        services.AddTransient<IAgentBridgeTool, LinkDiesTool>();
        services.AddTransient<IAgentBridgeTool, NewDesignTool>();
        services.AddTransient<IAgentBridgeTool, OpenDesignTool>();
        services.AddTransient<IAgentBridgeTool, ListComponentTypesTool>();
        services.AddTransient<IAgentBridgeTool, GetComponentTool>();
        services.AddTransient<IAgentBridgeTool, PlaceComponentTool>();
        services.AddTransient<IAgentBridgeTool, RotateComponentTool>();
        services.AddTransient<IAgentBridgeTool, ConnectPinsTool>();
        services.AddTransient<IAgentBridgeTool, SetParameterTool>();
        services.AddTransient<IAgentBridgeTool, SetLightSourceTool>();
        services.AddTransient<IAgentBridgeTool, SimulateTool>();
        services.AddTransient<IAgentBridgeTool>(sp => new SweepSpectrumTool(sp.GetRequiredService<AgentDesignSession>()));
        services.AddTransient<IAgentBridgeTool, SweepParameterTool>();
        services.AddTransient<IAgentBridgeTool, ValidateDesignTool>();
        services.AddTransient<IAgentBridgeTool, ExportGdsTool>();
        services.AddTransient<IAgentBridgeTool, ExportNetlistTool>();
        services.AddTransient<IAgentBridgeTool, AnalyzeGdsTool>();
        services.AddTransient<IAgentBridgeTool, ImportGdsTool>();
        services.AddTransient<IAgentBridgeTool, CreateGroupTool>();
        services.AddTransient<IAgentBridgeTool, UngroupTool>();
        services.AddTransient<IAgentBridgeTool, SavePrefabTool>();
        services.AddTransient<IAgentBridgeTool, DeletePrefabTool>();
        services.AddTransient<IAgentBridgeTool, MakeGateTool>();
        services.AddTransient<IAgentBridgeTool, LogicBuildTool>();
        services.AddTransient<IAgentBridgeTool, LogicEvaluateTool>();
        services.AddTransient<IAgentBridgeTool, LogicTruthTableTool>();
        services.AddTransient<IAgentBridgeTool, LogicStepTool>();
        services.AddTransient<IAgentBridgeTool, IsaRunTool>();
        services.AddTransient<IAgentBridgeTool, GetFloorplanTool>();
        services.AddTransient<IAgentBridgeTool, FindFreeSpaceTool>();
        services.AddTransient<IAgentBridgeTool, SetChipSizeTool>();
        services.AddTransient<IAgentBridgeTool, DeleteDieTool>();
        services.AddSingleton<AgentToolCatalog>();

        services.AddSingleton(sp => new McpJsonRpcDispatcher(
            sp.GetRequiredService<AgentToolCatalog>(), AppVersion()));
        services.AddSingleton(sp =>
        {
            var preferences = sp.GetRequiredService<UserPreferencesService>();
            return new McpHttpEndpoint(
                sp.GetRequiredService<McpJsonRpcDispatcher>(), preferences.GetRequiredAgentBridgeToken);
        });
        services.AddSingleton(sp => new McpHttpServer(sp.GetRequiredService<McpHttpEndpoint>(),
            error => sp.GetRequiredService<CAP_Core.ErrorConsoleService>().LogError(
                "Agent bridge cancellation callback failed after shutdown drained.", error)));
        services.AddSingleton<AgentBridgeHost>();
        services.AddSingleton<IAgentBridgeControl>(sp => sp.GetRequiredService<AgentBridgeHost>());
        services.AddSingleton(sp => new ClaudeCodeRegistrar(sp.GetRequiredService<CAP_Core.Export.ProcessLaunchFactory>()));
        services.AddSingleton<ClaudeCodeConnector>();
        services.AddSingleton<IAgentConnection>(sp => sp.GetRequiredService<ClaudeCodeConnector>());
        services.AddTransient<AgentBridgeSettingsViewModel>();
        services.AddSingleton(sp => new ClaudeCodeConnectPromptViewModel(
            sp.GetRequiredService<UserPreferencesService>(), sp.GetRequiredService<IAgentConnection>(), AppVersion(),
            sp.GetRequiredService<Services.Lifetime.ApplicationWorkCoordinator>()));

        return services;
    }

    private static string AppVersion() =>
        (Assembly.GetEntryAssembly() ?? typeof(AgentBridgeFeatureExtensions).Assembly)
            .GetName().Version?.ToString(3) ?? "0.0.0";
}
