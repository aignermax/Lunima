using CommunityToolkit.Mvvm.ComponentModel;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Converters;
using CAP.Avalonia.Services;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// ViewModel for the right sidebar panel.
/// Contains analysis, diagnostics, and validation features.
/// Max 250 lines per CLAUDE.md guideline.
/// </summary>
public partial class RightPanelViewModel : ObservableObject
{
    /// <summary>
    /// ViewModel for parameter sweep analysis.
    /// </summary>
    public ParameterSweepViewModel Sweep { get; }

    /// <summary>
    /// ViewModel for waveguide routing diagnostics (path finding performance).
    /// </summary>
    public RoutingDiagnosticsViewModel RoutingDiagnostics { get; }

    /// <summary>
    /// ViewModel for the Design Checks panel (validation and navigation of issues).
    /// </summary>
    public DesignValidationViewModel DesignValidation { get; }

    /// <summary>
    /// ViewModel for component dimension diagnostics (validation of GDS export dimensions).
    /// </summary>
    public ComponentDimensionDiagnosticsViewModel DimensionDiagnostics { get; }

    /// <summary>
    /// ViewModel for component dimension validation (checks bbox vs pin positions).
    /// </summary>
    public ComponentDimensionViewModel DimensionValidator { get; }

    /// <summary>
    /// ViewModel for end-to-end Nazca export validation.
    /// </summary>
    public ExportValidationViewModel ExportValidation { get; }

    /// <summary>
    /// ViewModel for S-Matrix performance diagnostics (sparsity analysis and memory usage).
    /// </summary>
    public SMatrixPerformanceViewModel SMatrixPerformance { get; }

    /// <summary>
    /// ViewModel for layout compression (minimize chip area while maintaining connectivity).
    /// </summary>
    public CompressLayoutViewModel CompressLayout { get; }

    public RightPanelViewModel(DesignCanvasViewModel canvas)
    {
        Sweep = new ParameterSweepViewModel();
        RoutingDiagnostics = new RoutingDiagnosticsViewModel();
        DesignValidation = new DesignValidationViewModel();
        DimensionDiagnostics = new ComponentDimensionDiagnosticsViewModel(canvas);
        DimensionValidator = new ComponentDimensionViewModel();
        ExportValidation = new ExportValidationViewModel();
        SMatrixPerformance = new SMatrixPerformanceViewModel();
        CompressLayout = new CompressLayoutViewModel();

        // Configure ViewModels that need canvas reference
        RoutingDiagnostics.Configure(canvas);
        DimensionValidator.Configure(canvas);
        CompressLayout.Configure(canvas);
    }
}
