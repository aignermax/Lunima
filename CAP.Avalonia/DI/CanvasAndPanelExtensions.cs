using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using Microsoft.Extensions.DependencyInjection;

namespace CAP.Avalonia.DI;

/// <summary>
/// Registers the design canvas, viewport, and all panel ViewModels.
/// </summary>
internal static class CanvasAndPanelExtensions
{
    /// <summary>
    /// Adds DesignCanvasViewModel, ViewportControlViewModel, and the three panel
    /// ViewModels together with all their sub-ViewModels.
    /// </summary>
    public static IServiceCollection AddCanvasAndPanels(this IServiceCollection services)
    {
        services.AddSingleton<DesignCanvasViewModel>();
        services.AddSingleton<ViewportControlViewModel>();

        // Left panel sub-ViewModels
        services.AddTransient<HierarchyPanelViewModel>();
        services.AddSingleton<PdkManagerViewModel>();
        services.AddTransient<ComponentLibraryViewModel>();

        // Right panel sub-ViewModels
        services.AddSingleton<ChipSizeViewModel>();
        services.AddTransient<ParameterSweepViewModel>();
        services.AddTransient<RoutingDiagnosticsViewModel>();
        services.AddTransient<DesignValidationViewModel>();
        services.AddTransient<ComponentDimensionDiagnosticsViewModel>();
        services.AddTransient<ComponentDimensionViewModel>();
        services.AddTransient<ExportValidationViewModel>();
        services.AddTransient<SMatrixPerformanceViewModel>();
        services.AddTransient<CompressLayoutViewModel>();
        services.AddTransient<GroupSMatrixViewModel>();
        services.AddTransient<ArchitectureReportViewModel>();
        services.AddTransient<PdkConsistencyViewModel>();

        // Bottom panel sub-ViewModels
        services.AddTransient<WaveguideLengthViewModel>();
        services.AddTransient<ElementLockViewModel>();
        services.AddTransient<ErrorConsoleViewModel>();

        // Panel host singletons
        services.AddSingleton<LeftPanelViewModel>();
        services.AddSingleton<RightPanelViewModel>();
        services.AddSingleton<BottomPanelViewModel>();

        return services;
    }
}
