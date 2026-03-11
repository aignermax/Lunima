using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using System.Collections.ObjectModel;
using System.Linq;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Diagnostics;

/// <summary>
/// Diagnostic ViewModel for verifying component dimensions in the design.
/// Shows component properties (Width, Height, Position, Rotation) for all components
/// to help validate that GDS export dimensions match the UI display.
/// Addresses issue #69: MMI 2x2 incorrect length in GDS export.
/// </summary>
public partial class ComponentDimensionDiagnosticsViewModel : ObservableObject
{
    private readonly DesignCanvasViewModel _canvas;

    [ObservableProperty]
    private string _diagnosticsText = "";

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private ObservableCollection<ComponentDimensionInfo> _componentDimensions = new();

    /// <summary>
    /// Initializes a new instance of the ComponentDimensionDiagnosticsViewModel.
    /// </summary>
    /// <param name="canvas">The design canvas to diagnose.</param>
    public ComponentDimensionDiagnosticsViewModel(DesignCanvasViewModel canvas)
    {
        _canvas = canvas;
    }

    /// <summary>
    /// Refreshes the diagnostic information for all components in the design.
    /// </summary>
    [RelayCommand]
    private void RefreshDiagnostics()
    {
        ComponentDimensions.Clear();

        foreach (var compVm in _canvas.Components)
        {
            var comp = compVm.Component;
            var info = new ComponentDimensionInfo
            {
                Identifier = comp.Identifier,
                NazcaFunction = comp.NazcaFunctionName ?? "N/A",
                WidthMicrometers = comp.WidthMicrometers,
                HeightMicrometers = comp.HeightMicrometers,
                PhysicalX = comp.PhysicalX,
                PhysicalY = comp.PhysicalY,
                RotationDegrees = comp.RotationDegrees,
                PinCount = comp.PhysicalPins.Count
            };
            ComponentDimensions.Add(info);
        }

        // Generate summary text using InvariantCulture for consistent number formatting
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var summary = $"Total Components: {ComponentDimensions.Count}\n\n";
        foreach (var dim in ComponentDimensions)
        {
            summary += $"{dim.Identifier}:\n";
            summary += $"  Nazca Function: {dim.NazcaFunction}\n";
            summary += $"  Dimensions: {dim.WidthMicrometers.ToString("F2", ci)} × {dim.HeightMicrometers.ToString("F2", ci)} µm\n";
            summary += $"  Position: ({dim.PhysicalX.ToString("F2", ci)}, {dim.PhysicalY.ToString("F2", ci)}) µm\n";
            summary += $"  Rotation: {dim.RotationDegrees.ToString("F1", ci)}°\n";
            summary += $"  Pins: {dim.PinCount}\n\n";
        }

        DiagnosticsText = summary;
    }

    /// <summary>
    /// Toggles the visibility of the diagnostics panel.
    /// </summary>
    [RelayCommand]
    private void ToggleVisibility()
    {
        IsVisible = !IsVisible;
        if (IsVisible)
        {
            RefreshDiagnostics();
        }
    }
}

/// <summary>
/// Diagnostic information for a single component.
/// </summary>
public class ComponentDimensionInfo
{
    public string Identifier { get; set; } = "";
    public string NazcaFunction { get; set; } = "";
    public double WidthMicrometers { get; set; }
    public double HeightMicrometers { get; set; }
    public double PhysicalX { get; set; }
    public double PhysicalY { get; set; }
    public double RotationDegrees { get; set; }
    public int PinCount { get; set; }
}
