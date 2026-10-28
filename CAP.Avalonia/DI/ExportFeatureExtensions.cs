using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Lifetime;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Export.PythonEnvironmentManager;
using CAP_Core.Export;
using CAP_Core.Export.Contracts;
using CAP_Core.Export.PythonEnvironmentManager;
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
        services.AddSingleton<GdsCoordinateExtractor>();
        services.AddSingleton<IGdsCoordinateExtractor>(sp => sp.GetRequiredService<GdsCoordinateExtractor>());
        services.AddSingleton<GdsCoordinateComparisonService>();
        services.AddSingleton<IGdsCoordinateComparer>(sp => sp.GetRequiredService<GdsCoordinateComparisonService>());
        services.AddSingleton<GdsCoordExtractViewModel>(sp => new(
            sp.GetRequiredService<IGdsCoordinateExtractor>(),
            sp.GetRequiredService<Services.Lifetime.ApplicationWorkCoordinator>()));
        services.AddSingleton<CAP.Avalonia.ViewModels.Diagnostics.GdsCoordinateComparisonViewModel>(sp => new(
            sp.GetRequiredService<IGdsCoordinateComparer>(),
            sp.GetRequiredService<Services.Lifetime.ApplicationWorkCoordinator>()));
        services.AddSingleton<GdsExportService>();
        services.AddSingleton<IGdsScriptRunner>(sp => sp.GetRequiredService<GdsExportService>());
        services.AddSingleton<IGdsExportSession>(sp => sp.GetRequiredService<GdsExportService>());
        services.AddSingleton<GdsExportViewModel>(sp =>
        {
            var vm = new GdsExportViewModel(
                sp.GetRequiredService<IGdsExportSession>(),
                sp.GetRequiredService<IPythonDiscovery>(),
                sp.GetRequiredService<CAP_Core.ErrorConsoleService>(),
                sp.GetService<ApplicationWorkCoordinator>());
            var prefs = sp.GetRequiredService<UserPreferencesService>();
            vm.Initialize(prefs.GetCustomPythonPath());
            vm.OnPythonPathChanged = path => prefs.SetCustomPythonPath(path);

            // Managed-environment integration (lazy resolution — the registry and the
            // env-manager ViewModel are resolved when the delegate fires, not at build time).
            vm.ManagedEnvironmentsProvider = () =>
                sp.GetRequiredService<PythonEnvironmentRegistry>().GetAll()
                    .Where(e => e.IsHealthy)
                    .Select(e => new ManagedEnvCandidate(
                        e.Name,
                        e.PythonExecutable,
                        $"Managed · {e.Name} · Python {e.PythonVersion ?? "?"} · Nazca {e.NazcaVersion ?? "?"}"))
                    .ToList();
            vm.ActivateManagedEnvironment = name =>
                sp.GetRequiredService<PythonEnvironmentRegistry>().SetActive(name);
            vm.RequestNazcaInstall = () =>
                _ = sp.GetRequiredService<PythonEnvironmentManagerViewModel>()
                    .StartDefaultNazcaInstallAsync();
            return vm;
        });

        services.AddSingleton<PhotonTorchExporter>();
        services.AddSingleton<PhotonTorchExportViewModel>();

        services.AddSingleton<GdsFactoryExportViewModel>(sp =>
        {
            var vm = new GdsFactoryExportViewModel(
                sp.GetRequiredService<IUrlLauncher>(),
                sp.GetRequiredService<CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel>(),
                sp.GetRequiredService<IGdsScriptRunner>(),
                sp.GetService<CAP_Core.ErrorConsoleService>(),
                sp.GetService<ApplicationWorkCoordinator>());
            // Auto-install gdsfactory on export (env-manager slice resolved lazily so the
            // export slice never imports it directly).
            vm.EnsureGdsFactoryAsync = (progress, ct) =>
                sp.GetRequiredService<PythonEnvironmentManagerViewModel>()
                    .EnsureGdsFactoryInstalledAsync(progress, ct);
            // Mixed-backend export: the classifier needs the loaded library
            // (raw-code backend lookup) and the configured nazca exporter (lazy resolution).
            vm.TemplateLibraryProvider = () =>
                sp.GetRequiredService<CAP.Avalonia.ViewModels.Panels.LeftPanelViewModel>().AllTemplates;
            vm.NazcaExporterProvider = () => sp.GetRequiredService<SimpleNazcaExporter>();
            return vm;
        });

        services.AddSingleton<VerilogAExporter>();
        services.AddSingleton<VerilogAFileWriter>();
        services.AddSingleton<IVerilogAExportWriter>(sp => sp.GetRequiredService<VerilogAFileWriter>());
        services.AddSingleton<VerilogAExportViewModel>();

        services.AddSingleton<SaxExporter>();

        // openEBL submission checker (headless core service, #1355)
        services.AddSingleton<CAP_Core.Export.OpenEbl.OpenEblSubmissionChecker>();
        services.AddSingleton<IOpenEblSubmissionChecker>(sp =>
            sp.GetRequiredService<CAP_Core.Export.OpenEbl.OpenEblSubmissionChecker>());

        // "Check for openEBL…" dialog (#1361) — singleton so a reopened dialog keeps the inputs.
        services.AddSingleton<CAP.Avalonia.ViewModels.Export.OpenEbl.OpenEblCheckViewModel>(sp =>
            new CAP.Avalonia.ViewModels.Export.OpenEbl.OpenEblCheckViewModel(
                sp.GetRequiredService<CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel>(),
                sp.GetRequiredService<SimpleNazcaExporter>(),
                sp.GetRequiredService<IGdsScriptRunner>(),
                sp.GetRequiredService<IOpenEblSubmissionChecker>(),
                sp.GetService<CAP_Core.ErrorConsoleService>(),
                sp.GetService<ApplicationWorkCoordinator>()));

        // Netlist view/export (gdsfactory YAML, issue #687)
        services.AddSingleton<CAP.Avalonia.ViewModels.Export.Netlist.NetlistViewModel>();

        return services;
    }
}
