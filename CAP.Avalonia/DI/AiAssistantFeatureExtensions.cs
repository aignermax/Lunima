using System.Net.Http;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AiTools;
using CAP.Avalonia.Services.AiTools.GridTools;
using CAP.Avalonia.ViewModels.AI;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using Microsoft.Extensions.DependencyInjection;

namespace CAP.Avalonia.DI;

/// <summary>
/// Registers the AI assistant services, tools, and ViewModel.
/// </summary>
internal static class AiAssistantFeatureExtensions
{
    /// <summary>
    /// Adds the AI service, all registered <see cref="IAiTool"/> implementations,
    /// the tool registry, and the <see cref="AiAssistantViewModel"/>.
    /// To add a new AI tool: add one <c>AddTransient&lt;IAiTool, YourTool&gt;()</c> line here.
    /// </summary>
    public static IServiceCollection AddAiAssistantFeature(this IServiceCollection services)
    {
        services.AddSingleton<IAiService, AiService>(sp =>
            new AiService(sp.GetRequiredService<HttpClient>()));

        services.AddSingleton<IAiGridService>(sp => new AiGridService(
            sp.GetRequiredService<DesignCanvasViewModel>(),
            sp.GetRequiredService<LeftPanelViewModel>(),
            sp.GetRequiredService<SimulationService>()));

        services.AddTransient<IAiTool, GetGridStateTool>();
        services.AddTransient<IAiTool, GetAvailableTypesTool>();
        services.AddTransient<IAiTool, PlaceComponentTool>();
        services.AddTransient<IAiTool, CreateConnectionTool>();
        services.AddTransient<IAiTool, RunSimulationTool>();
        services.AddTransient<IAiTool, GetLightValuesTool>();
        services.AddTransient<IAiTool, ClearGridTool>();
        services.AddTransient<IAiTool, CreateGroupTool>();
        services.AddTransient<IAiTool, UngroupTool>();
        services.AddTransient<IAiTool, SaveAsPrefabTool>();
        services.AddTransient<IAiTool, InspectGroupTool>();
        services.AddTransient<IAiTool, CopyComponentTool>();
        services.AddTransient<IAiTool, FitToViewTool>();
        services.AddSingleton<IAiToolRegistry, AiToolRegistry>();

        services.AddSingleton<AiAssistantViewModel>(sp => new AiAssistantViewModel(
            sp.GetRequiredService<IAiService>(),
            sp.GetRequiredService<UserPreferencesService>(),
            sp.GetRequiredService<IAiToolRegistry>()));

        return services;
    }
}
