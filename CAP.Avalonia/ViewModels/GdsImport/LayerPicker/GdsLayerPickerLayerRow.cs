using Avalonia.Media;
using CAP.Avalonia.Services.Localization;
using CAP_DataAccess.Import.Gds.LayerPicker;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.GdsImport.LayerPicker;

/// <summary>
/// One layer of the click-to-assign picker's legend: the (layer, datatype)
/// pair with its display color, geometry counts and the currently assigned
/// type. Selectable both by clicking the geometry and by clicking the row.
/// </summary>
public sealed partial class GdsLayerPickerLayerRow : ObservableObject
{
    /// <summary>The geometry group behind this row.</summary>
    public GdsLayerGeometry Geometry { get; }

    /// <summary>The (layer, datatype) pair this row represents.</summary>
    public GdsLayerPair Pair => Geometry.Pair;

    /// <summary>Display color used for this layer's geometry and legend swatch.</summary>
    public Color Color { get; }

    /// <summary>Legend swatch brush in this layer's color.</summary>
    public IBrush Swatch { get; }

    /// <summary>The pair in census notation, e.g. <c>(56,0)</c>.</summary>
    public string PairText => Pair.ToString();

    /// <summary>Compact geometry counts, e.g. <c>12 shapes · 2 labels</c>.</summary>
    public string CountsText { get; }

    /// <summary>True while this row's layer is the picker's selection.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Localized name of the assigned type, or the "not assigned" dash.</summary>
    [ObservableProperty]
    private string _assignmentText = "";

    /// <summary>Initializes a row for one geometry group.</summary>
    public GdsLayerPickerLayerRow(GdsLayerGeometry geometry, Color color)
    {
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        Color = color;
        Swatch = new SolidColorBrush(color);
        CountsText = string.Format(
            LocalizationService.Instance.Translate("GdsImport.LayerPickerRowCountsFormat"),
            geometry.Polygons.Count, geometry.Texts.Count);
    }

    /// <summary>Updates <see cref="AssignmentText"/> from the picker's assignment state.</summary>
    public void SetAssignment(GdsLayerFieldTarget? target)
    {
        AssignmentText = LocalizationService.Instance.Translate(target switch
        {
            GdsLayerFieldTarget.Waveguide => "GdsImport.SuggestionRoleWaveguide",
            GdsLayerFieldTarget.Metal => "GdsImport.SuggestionRoleMetal",
            GdsLayerFieldTarget.PortLabels => "GdsImport.SuggestionRolePortLabels",
            _ => "GdsImport.LayerPickerNotAssigned",
        });
    }
}
