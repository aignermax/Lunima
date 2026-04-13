using CommunityToolkit.Mvvm.ComponentModel;
using CAP_Core.Export;

namespace CAP.Avalonia.ViewModels.PdkImport;

/// <summary>
/// ViewModel representing a single parsed component in the PDK Import Wizard.
/// Displays status, pin count, and dimensions for user review and selection.
/// </summary>
public partial class ComponentParseResultViewModel : ObservableObject
{
    /// <summary>Component name (e.g., "ebeam_y_1550").</summary>
    public string Name { get; }

    /// <summary>Component category for library grouping (e.g., "Couplers").</summary>
    public string Category { get; }

    /// <summary>Number of pins found for this component.</summary>
    public int PinCount { get; }

    /// <summary>Bounding box width in micrometers.</summary>
    public double WidthMicrometers { get; }

    /// <summary>Bounding box height in micrometers.</summary>
    public double HeightMicrometers { get; }

    /// <summary>Whether the component has warnings (e.g., no pins detected).</summary>
    public bool HasWarnings => PinCount == 0;

    /// <summary>
    /// Human-readable status text shown in the component list.
    /// Shows pin count for valid components, or a warning for components without pins.
    /// </summary>
    public string StatusText => PinCount > 0
        ? $"✓ {PinCount} pin(s)"
        : "⚠ No pins";

    /// <summary>
    /// Display color for the status text.
    /// Green for successfully parsed components, amber for warnings.
    /// </summary>
    public string StatusColor => PinCount > 0 ? "#4CAF50" : "#FF9800";

    /// <summary>
    /// Formatted dimensions string for display (e.g., "10.5 × 25.3 µm").
    /// </summary>
    public string DimensionsText =>
        $"{WidthMicrometers:F1} × {HeightMicrometers:F1} µm";

    /// <summary>Whether this component is selected for import.</summary>
    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>
    /// Initializes a new <see cref="ComponentParseResultViewModel"/> from parsed geometry.
    /// </summary>
    /// <param name="geometry">The parsed component geometry from the Python script.</param>
    public ComponentParseResultViewModel(ParsedComponentGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        Name = geometry.Name;
        Category = geometry.Category;
        PinCount = geometry.Pins?.Count ?? 0;
        WidthMicrometers = geometry.WidthMicrometers;
        HeightMicrometers = geometry.HeightMicrometers;
    }
}
