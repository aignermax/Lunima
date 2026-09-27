using CAP.Avalonia.ViewModels.GdsImport.LayerPicker;
using CAP_DataAccess.Import.Gds.LayerPicker;

namespace CAP.Avalonia.ViewModels.GdsImport;

/// <summary>
/// The click-to-assign layer-picker half of
/// <see cref="GdsImportDialogViewModel"/> (issue #1173): builds the picker over
/// the analyzed library's selected top cell, seeds it with the current field
/// state and applies its decisions back to the layer fields. One pair lives in
/// exactly one field — assigning a type moves it, "ignore" removes it.
/// </summary>
public partial class GdsImportDialogViewModel
{
    private static readonly GdsLayerFieldTarget[] AllLayerFieldTargets =
    {
        GdsLayerFieldTarget.PortLabels,
        GdsLayerFieldTarget.Waveguide,
        GdsLayerFieldTarget.Metal,
    };

    /// <summary>
    /// Creates the picker ViewModel for the selected top cell, or null when
    /// analysis has not finished, no top cell is selected, or the cell has no
    /// geometry to click.
    /// </summary>
    public GdsLayerPickerViewModel? CreateLayerPicker()
    {
        if (_analyzedLibrary is null || SelectedTopCell is null)
            return null;
        GdsLayerGeometryScene scene;
        try
        {
            scene = GdsLayerGeometryScene.Build(_analyzedLibrary, SelectedTopCell.CellName);
        }
        catch (InvalidDataException ex)
        {
            _errorConsole?.LogError("GDS layer picker: could not flatten the top cell", ex);
            return null;
        }
        if (scene.Layers.Count == 0)
            return null;
        return new GdsLayerPickerViewModel(scene, ReadCurrentAssignments(), ApplyPickedAssignment);
    }

    /// <summary>
    /// Snapshot of the fields as pair → field map. When a pair appears in more
    /// than one field the first target in port/waveguide/metal order wins —
    /// the picker then re-assigns it unambiguously. Malformed field texts
    /// contribute nothing (they surface via the existing import-time error).
    /// </summary>
    private Dictionary<GdsLayerPair, GdsLayerFieldTarget> ReadCurrentAssignments()
    {
        var assignments = new Dictionary<GdsLayerPair, GdsLayerFieldTarget>();
        foreach (var target in AllLayerFieldTargets)
        {
            var pairs = ParseLayerPairs(GetFieldText(target));
            if (pairs is null)
                continue;
            foreach (var (layer, datatype) in pairs)
                assignments.TryAdd(new GdsLayerPair(layer, datatype), target);
        }
        return assignments;
    }

    /// <summary>
    /// Applies one picker decision: the pair is removed from every layer field
    /// and, unless <paramref name="target"/> is null (= ignore), appended to
    /// its new field.
    /// </summary>
    private void ApplyPickedAssignment(GdsLayerPair pair, GdsLayerFieldTarget? target)
    {
        foreach (var field in AllLayerFieldTargets)
            RemoveLayerPair(field, pair.Layer, pair.Datatype);
        if (target is not null)
            AppendLayerPair(target.Value, pair.Layer, pair.Datatype);
    }
}
