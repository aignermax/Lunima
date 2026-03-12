using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Simulation;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// ViewModel for the right panel containing properties, parameter sweep, and diagnostic panels.
/// </summary>
public partial class RightPanelViewModel : ObservableObject
{
    /// <summary>
    /// Currently selected component.
    /// </summary>
    [ObservableProperty]
    private ComponentViewModel? _selectedComponent;

    /// <summary>
    /// Currently selected waveguide connection.
    /// </summary>
    [ObservableProperty]
    private WaveguideConnectionViewModel? _selectedWaveguideConnection;

    /// <summary>
    /// Available wavelength options for laser configuration dropdown.
    /// </summary>
    public IReadOnlyList<WavelengthOption> WavelengthOptions { get; } = WavelengthOption.All;

    /// <summary>
    /// Parameter sweep ViewModel for analyzing component parameters.
    /// </summary>
    public ParameterSweepViewModel Sweep { get; } = new();

    /// <summary>
    /// Design validation ViewModel for checking design issues.
    /// </summary>
    public DesignValidationViewModel DesignValidation { get; } = new();

    /// <summary>
    /// Routing diagnostics ViewModel for analyzing routing issues.
    /// </summary>
    public RoutingDiagnosticsViewModel RoutingDiagnostics { get; } = new();

    /// <summary>
    /// Component dimension diagnostics ViewModel for validating GDS export dimensions.
    /// </summary>
    [ObservableProperty]
    private ComponentDimensionDiagnosticsViewModel? _dimensionDiagnostics;

    /// <summary>
    /// Component dimension validator ViewModel for checking bbox vs pin positions.
    /// </summary>
    public ComponentDimensionViewModel DimensionValidator { get; } = new();

    /// <summary>
    /// Export validation ViewModel for end-to-end Nazca export validation.
    /// </summary>
    public ExportValidationViewModel ExportValidation { get; } = new();

    /// <summary>
    /// S-Matrix performance diagnostics ViewModel.
    /// </summary>
    public SMatrixPerformanceViewModel SMatrixPerformance { get; } = new();

    /// <summary>
    /// Layout compression ViewModel.
    /// </summary>
    public CompressLayoutViewModel CompressLayout { get; } = new();

    /// <summary>
    /// Waveguide length ViewModel for parameterized waveguide lengths.
    /// </summary>
    public WaveguideLengthViewModel WaveguideLength { get; } = new();

    partial void OnSelectedComponentChanged(ComponentViewModel? value)
    {
        Sweep.ConfigureForComponent(value, null); // Canvas will be wired up by MainViewModel
    }

    partial void OnSelectedWaveguideConnectionChanged(WaveguideConnectionViewModel? value)
    {
        WaveguideLength.SelectedConnection = value;
        if (value != null)
        {
            WaveguideLength.UpdateLengthStatus();
        }
    }
}
