using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Export;
using CAP_Core.Export;
using Microsoft.Extensions.DependencyInjection;

namespace CAP.Avalonia.DI;

/// <summary>
/// Registers all export format services and ViewModels (GDS, PhotonTorch, VerilogA).
/// All export ViewModels are Singletons so that dialog DataContexts and
/// FileOperations commands share the same instance and state.
/// </summary>
internal static class ExportFeatureExtensions
{
    /// <summary>
    /// Adds GDS, PhotonTorch, and VerilogA export services and ViewModels.
    /// </summary>
    public static IServiceCollection AddExportFeature(this IServiceCollection services)
    {
        services.AddSingleton<GdsExportService>();
        services.AddSingleton<GdsExportViewModel>(sp =>
        {
            var vm = new GdsExportViewModel(
                sp.GetRequiredService<GdsExportService>(),
                sp.GetRequiredService<CAP_Core.ErrorConsoleService>());
            var prefs = sp.GetRequiredService<UserPreferencesService>();
            vm.Initialize(prefs.GetCustomPythonPath());
            vm.OnPythonPathChanged = path => prefs.SetCustomPythonPath(path);
            return vm;
        });

        services.AddSingleton<PhotonTorchExporter>();
        services.AddSingleton<PhotonTorchExportViewModel>();

        services.AddSingleton<VerilogAExporter>();
        services.AddSingleton<VerilogAFileWriter>();
        services.AddSingleton<VerilogAExportViewModel>();

        services.AddSingleton<SaxExporter>();

        return services;
    }
}
