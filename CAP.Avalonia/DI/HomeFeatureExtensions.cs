using CAP_Core.Preferences;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Home;
using Microsoft.Extensions.DependencyInjection;

namespace CAP.Avalonia.DI;

/// <summary>
/// Registers the Home screen feature: the startup project menu with the
/// recent-projects list, shipped example designs, and reopen-last preference.
/// </summary>
internal static class HomeFeatureExtensions
{
    /// <summary>
    /// Adds the recent-projects store, example-design discovery, and the
    /// Home screen ViewModel.
    /// </summary>
    public static IServiceCollection AddHomeFeature(this IServiceCollection services)
    {
        services.AddSingleton<IRecentProjectPreferences>(sp => sp.GetRequiredService<UserPreferencesService>());
        services.AddSingleton<IHomePreferences>(sp => sp.GetRequiredService<UserPreferencesService>());
        services.AddSingleton<ILearningPreferences>(sp => sp.GetRequiredService<UserPreferencesService>());
        services.AddSingleton<RecentProjectsService>();
        services.AddExampleCatalog();
        services.AddSingleton<HomeViewModel>();

        return services;
    }
}
