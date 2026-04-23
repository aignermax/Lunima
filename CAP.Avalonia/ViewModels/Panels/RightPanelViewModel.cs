using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Controls;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Converters;
using CAP.Avalonia.ViewModels.Update;
using CAP.Avalonia.ViewModels.AI;
using CAP.Avalonia.Services;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// ViewModel for the right sidebar panel.
/// Contains analysis, diagnostics, and validation features.
/// Max 250 lines per CLAUDE.md guideline.
/// </summary>
public partial class RightPanelViewModel : ObservableObject
{
    private readonly UserPreferencesService _preferencesService;

    private GridLength _rightPanelWidth = new GridLength(250);
    /// <summary>
    /// Width of the right panel in pixels. Persisted in user preferences.
    /// Clamped to [200, 800] range.
    /// </summary>
    public GridLength RightPanelWidth
    {
        get => _rightPanelWidth;
        set
        {
            // Clamp to reasonable values (min 200, max 800)
            var clampedValue = Math.Max(200, Math.Min(800, value.Value));
            var newGridLength = new GridLength(clampedValue);
            if (SetProperty(ref _rightPanelWidth, newGridLength))
            {
                SaveRightPanelWidth();
            }
        }
    }

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

    /// <summary>
    /// ViewModel for ComponentGroup S-Matrix diagnostics (shows matrix computation status).
    /// </summary>
    public GroupSMatrixViewModel GroupSMatrix { get; }

    /// <summary>
    /// ViewModel for the Architecture Report panel (metrics, SOLID compliance, recommendations).
    /// </summary>
    public ArchitectureReportViewModel ArchitectureReport { get; }

    /// <summary>
    /// ViewModel for the PDK Consistency panel (validates JSON PDK definitions vs Nazca).
    /// Issue #334: Investigate PDK JSON vs Nazca Python coordinate mismatch.
    /// </summary>
    public PdkConsistencyViewModel PdkConsistency { get; }

    /// <summary>
    /// ViewModel for the software update panel (check for updates, download, install).
    /// </summary>
    public UpdateViewModel Update { get; }

    /// <summary>
    /// ViewModel for the in-app AI Design Assistant chat panel.
    /// </summary>
    public AiAssistantViewModel AiAssistant { get; }

    /// <summary>Initializes a new instance of <see cref="RightPanelViewModel"/>.</summary>
    public RightPanelViewModel(
        DesignCanvasViewModel canvas,
        UserPreferencesService preferencesService,
        ParameterSweepViewModel sweep,
        RoutingDiagnosticsViewModel routingDiagnostics,
        DesignValidationViewModel designValidation,
        ComponentDimensionDiagnosticsViewModel dimensionDiagnostics,
        ComponentDimensionViewModel dimensionValidator,
        ExportValidationViewModel exportValidation,
        SMatrixPerformanceViewModel sMatrixPerformance,
        CompressLayoutViewModel compressLayout,
        GroupSMatrixViewModel groupSMatrix,
        ArchitectureReportViewModel architectureReport,
        PdkConsistencyViewModel pdkConsistency,
        UpdateViewModel update,
        AiAssistantViewModel aiAssistant)
    {
        _preferencesService = preferencesService;

        Sweep = sweep;
        RoutingDiagnostics = routingDiagnostics;
        DesignValidation = designValidation;
        DimensionDiagnostics = dimensionDiagnostics;
        DimensionValidator = dimensionValidator;
        ExportValidation = exportValidation;
        SMatrixPerformance = sMatrixPerformance;
        CompressLayout = compressLayout;
        GroupSMatrix = groupSMatrix;
        ArchitectureReport = architectureReport;
        PdkConsistency = pdkConsistency;
        Update = update;
        AiAssistant = aiAssistant;

        // Configure ViewModels that need canvas reference
        RoutingDiagnostics.Configure(canvas);
        DimensionValidator.Configure(canvas);
        CompressLayout.Configure(canvas);
    }

    /// <summary>
    /// Initializes the panel (loads saved width from preferences).
    /// </summary>
    public void Initialize()
    {
        RestoreRightPanelWidth();
    }

    private void RestoreRightPanelWidth()
    {
        var width = _preferencesService.GetRightPanelWidth();
        RightPanelWidth = new GridLength(width);
    }

    private void SaveRightPanelWidth()
    {
        _preferencesService.SetRightPanelWidth(RightPanelWidth.Value);
    }
}
