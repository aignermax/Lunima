using CAP.Avalonia.Services;
using CAP_Contracts;
using CAP_Core.Helpers;
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
        services.AddSingleton<IDataAccessor, FileDataAccessor>();
        services.AddSingleton<CAP_Core.Components.Creation.GroupLibraryManager>();
        services.AddSingleton<CAP_Core.ErrorConsoleService>();
        services.AddSingleton<SimpleNazcaExporter>();

        services.AddSingleton<SimulationService>();
        services.AddSingleton<PdkLoader>();
        services.AddSingleton<PdkImportService>();
        services.AddSingleton<Commands.CommandManager>();
        services.AddSingleton<UserPreferencesService>();
        services.AddSingleton<ProjectPersistenceService>();
        services.AddSingleton<UserSMatrixOverrideStore>(sp =>
            new UserSMatrixOverrideStore(sp.GetService<CAP_Core.ErrorConsoleService>()));
        services.AddSingleton<Services.GroupPreviewGenerator>();
        services.AddSingleton<IInputDialogService, InputDialogService>();
        services.AddSingleton<IPortMappingDialogService, PortMappingDialogService>();

        return services;
    }
}
