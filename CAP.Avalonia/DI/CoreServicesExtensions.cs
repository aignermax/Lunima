using CAP.Avalonia.Services;
using CAP_Contracts;
using CAP_Core.Helpers;
using CAP_Core.Preferences;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Microsoft.Extensions.DependencyInjection;

namespace CAP.Avalonia.DI;

/// <summary>
/// Registers core domain services, persistence, and application-wide infrastructure.
/// </summary>
internal static class CoreServicesExtensions
{
    /// <summary>
    /// Adds data access, serialization, simulation, PDK, commands, preferences,
    /// and other application-wide services.
    /// </summary>
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<Services.Lifetime.ApplicationWorkCoordinator>();
        services.AddSingleton<IDataAccessor, FileDataAccessor>();
        services.AddSingleton<CAP_DataAccess.Persistence.NativeDesign.INativeDesignFileStore,
            CAP_DataAccess.Persistence.NativeDesign.NativeDesignFileStore>();
        services.AddSingleton<CAP_Core.Components.Creation.GroupLibraryManager>();
        services.AddSingleton<CAP_Core.Components.Creation.IGroupLibrary>(sp =>
            sp.GetRequiredService<CAP_Core.Components.Creation.GroupLibraryManager>());

        // Platform-aware external-process launching (Python, Docker). The prober supplies
        // well-known interpreter/Docker locations and an augmented PATH so subprocesses
        // resolve correctly even when the app is launched from Finder/Dock on macOS (which
        // does not inherit a shell-initialised PATH). Registered as shared singletons so
        // every export/preview/solver service uses the same resolution.
        services.AddSingleton<CAP_Core.Export.ExecutablePathProber>();
        services.AddSingleton<CAP_Core.Export.ProcessLaunchFactory>();
        services.AddSingleton<CAP_Core.ErrorConsoleService>();
        services.AddSingleton<CAP_Contracts.Logger.IDiagnosticSink>(sp => sp.GetRequiredService<CAP_Core.ErrorConsoleService>());
        // Exporter reads the global interconnect settings (width/radius/layer, issue #574)
        // lazily at export time so Settings-window changes apply without restart.
        services.AddSingleton(sp => new SimpleNazcaExporter
        {
            SettingsSource = () => sp.GetRequiredService<UserPreferencesService>().GetInterconnectSettings(),
        });

        services.AddSingleton<SimulationService>();
        services.AddSingleton<PdkLoader>();
        services.AddSingleton<IPdkReader>(sp => sp.GetRequiredService<PdkLoader>());
        services.AddSingleton<PdkImportService>();
        services.AddSingleton<IPdkImportWorkflow>(sp => sp.GetRequiredService<PdkImportService>());
        services.AddSingleton<IProcessImporter, UpdkYamlProcessImporter>();
        services.AddSingleton<IProcessImporter, NazcaCsvProcessImporter>();
        services.AddSingleton(sp => new Views.Shell.ProcessEditorFactory(
            sp.GetServices<IProcessImporter>().ToArray(), sp.GetRequiredService<IPdkWriter>(),
            sp.GetRequiredService<CAP_DataAccess.Components.AddCustomComponent.IUserPdkLibrary>()));
        services.AddSingleton<Commands.CommandManager>();
        services.AddSingleton<UserPreferencesService>();
        services.AddSingleton<IPdkLibraryPreferences>(sp => sp.GetRequiredService<UserPreferencesService>());
        services.AddSingleton<IPanelLayoutPreferences>(sp => sp.GetRequiredService<UserPreferencesService>());
        services.AddSingleton<IPythonInterpreterPreferences>(sp => sp.GetRequiredService<UserPreferencesService>());
        services.AddSingleton<IFdtdPreferences>(sp => sp.GetRequiredService<UserPreferencesService>());
        services.AddSingleton<IAgentBridgePreferences>(sp => sp.GetRequiredService<UserPreferencesService>());
        services.AddSingleton<IAgentConnectionPromptPreferences>(sp => sp.GetRequiredService<UserPreferencesService>());
        services.AddSingleton<IAiPreferences>(sp => sp.GetRequiredService<UserPreferencesService>());
        services.AddSingleton<IChipSizePreferences>(sp => sp.GetRequiredService<UserPreferencesService>());
        services.AddSingleton<IInterconnectPreferences>(sp => sp.GetRequiredService<UserPreferencesService>());
        services.AddSingleton<ILanguagePreferences>(sp => sp.GetRequiredService<UserPreferencesService>());
        services.AddSingleton<UserSMatrixOverrideStore>(sp =>
            new UserSMatrixOverrideStore(sp.GetService<CAP_Contracts.Logger.IDiagnosticSink>()));
        services.AddSingleton<CAP_DataAccess.Persistence.PIR.ITemplateSMatrixStore>(sp =>
            sp.GetRequiredService<UserSMatrixOverrideStore>());
        services.AddSingleton<Services.GroupPreviewGenerator>();
        services.AddSingleton<IInputDialogService, InputDialogService>();
        services.AddSingleton<IPortMappingDialogService, PortMappingDialogService>();

        return services;
    }
}
