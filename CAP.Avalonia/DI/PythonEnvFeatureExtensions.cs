using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Export.PythonEnvironmentManager;
using CAP_Core.Export;
using CAP_Core.Export.PythonEnvironmentManager;
using Microsoft.Extensions.DependencyInjection;

namespace CAP.Avalonia.DI;

/// <summary>
/// Registers the managed Python environment feature services and ViewModel.
/// The registry's <c>OnActiveEnvironmentChanged</c> callback is wired to
/// <see cref="UserPreferencesService.SetCustomPythonPath"/> so the active
/// environment's interpreter is picked up by export and preview pipelines.
/// </summary>
internal static class PythonEnvFeatureExtensions
{
    /// <summary>
    /// Adds core services (UvBootstrapper, NazcaPackageInstaller, PythonEnvironmentRegistry,
    /// EnvironmentHealthChecker) and the PythonEnvironmentManagerViewModel.
    /// </summary>
    public static IServiceCollection AddPythonEnvFeature(this IServiceCollection services)
    {
        services.AddSingleton<UvBootstrapper>();
        services.AddSingleton<NazcaPackageInstaller>();
        services.AddSingleton<PythonDiscoveryService>();
        services.AddSingleton<EnvironmentHealthChecker>();

        services.AddSingleton<PythonEnvironmentRegistry>(sp =>
        {
            var registry = new PythonEnvironmentRegistry();
            var prefs = sp.GetRequiredService<UserPreferencesService>();
            registry.OnActiveEnvironmentChanged = path => prefs.SetCustomPythonPath(path);
            return registry;
        });

        services.AddSingleton<PythonEnvironmentManagerViewModel>();

        return services;
    }
}
