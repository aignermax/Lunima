using CAP.Avalonia.Services.LiveState;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.LiveState;
using Microsoft.Extensions.DependencyInjection;

namespace CAP.Avalonia.DI;

/// <summary>
/// Registers the agent live-state feature: a localhost HTTP/JSON server
/// exposing canvas, error-console and simulation state to external MCP agents.
/// </summary>
internal static class LiveStateFeatureExtensions
{
    /// <summary>Adds the live-state API, HTTP server, and its window ViewModel.</summary>
    public static IServiceCollection AddLiveStateFeature(this IServiceCollection services)
    {
        services.AddSingleton<LiveStateCommandExecutor>();
        // MainViewModel is resolved lazily: the API depends on it, but MainViewModel
        // itself must not depend on the agent feature (no cycle).
        services.AddSingleton(sp => new LiveStateApi(
            () => sp.GetRequiredService<MainViewModel>(),
            sp.GetRequiredService<CAP_Core.ErrorConsoleService>(),
            sp.GetRequiredService<LiveStateCommandExecutor>()));
        services.AddSingleton<LiveStateHttpServer>();
        services.AddSingleton<AgentServerViewModel>();
        return services;
    }
}
