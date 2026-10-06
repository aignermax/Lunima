using System.Collections.ObjectModel;
using Avalonia.Media;
using CAP.Avalonia.Services.Localization;
using CAP_DataAccess.Import.Gds.LayerPicker;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.GdsImport.LayerPicker;

/// <summary>
/// ViewModel for the click-to-assign layer picker (issue #1173): the user
/// clicks the imported geometry to select a (layer, datatype) pair, then
/// assigns its type (waveguide / metal / port labels) or marks it ignored.
/// Assignments apply immediately to the import dialog's layer fields through
/// the callback — closing the picker never discards anything.
/// </summary>
public sealed partial class GdsLayerPickerViewModel : ObservableObject
{
    /// <summary>Golden-angle hue step giving well-separated colors for any layer count.</summary>
    private const double PaletteHueStepDegrees = 137.508;

    private readonly Dictionary<GdsLayerPair, GdsLayerFieldTarget> _assignments;
    private readonly Action<GdsLayerPair, GdsLayerFieldTarget?> _applyAssignment;

    /// <summary>The flattened geometry the picker renders and hit-tests.</summary>
    public GdsLayerGeometryScene Scene { get; }

    /// <summary>Legend rows, one per (layer, datatype) pair, sorted by layer.</summary>
    public ObservableCollection<GdsLayerPickerLayerRow> Rows { get; } = new();

    /// <summary>The currently selected layer row, or null before the first pick.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectionText))]
    [NotifyCanExecuteChangedFor(nameof(AssignWaveguideCommand))]
    [NotifyCanExecuteChangedFor(nameof(AssignMetalCommand))]
    [NotifyCanExecuteChangedFor(nameof(AssignPortLabelsCommand))]
    [NotifyCanExecuteChangedFor(nameof(AssignIgnoreCommand))]
    private GdsLayerPickerLayerRow? _selectedRow;

    /// <summary>True while a layer is selected (enables the assignment buttons).</summary>
    public bool HasSelection => SelectedRow is not null;

    /// <summary>Status line naming the selected layer, or the "click the geometry" hint.</summary>
    public string SelectionText => SelectedRow is null
        ? LocalizationService.Instance.Translate("GdsImport.LayerPickerNoSelection")
        : string.Format(
            LocalizationService.Instance.Translate("GdsImport.LayerPickerSelectedFormat"),
            SelectedRow.PairText);

    /// <summary>Initializes the picker over a built scene.</summary>
    /// <param name="scene">The flattened top-cell geometry grouped per layer.</param>
    /// <param name="initialAssignments">Current field state: which pair sits in which layer field.</param>
    /// <param name="applyAssignment">
    /// Applies one decision to the dialog's fields; a null target means "ignore"
    /// (remove the pair from every field).
    /// </param>
    public GdsLayerPickerViewModel(
        GdsLayerGeometryScene scene,
        IReadOnlyDictionary<GdsLayerPair, GdsLayerFieldTarget> initialAssignments,
        Action<GdsLayerPair, GdsLayerFieldTarget?> applyAssignment)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
        ArgumentNullException.ThrowIfNull(initialAssignments);
        _applyAssignment = applyAssignment ?? throw new ArgumentNullException(nameof(applyAssignment));
        _assignments = initialAssignments.ToDictionary(kv => kv.Key, kv => kv.Value);

        for (var i = 0; i < scene.Layers.Count; i++)
        {
            var row = new GdsLayerPickerLayerRow(scene.Layers[i], PaletteColor(i));
            row.SetAssignment(_assignments.TryGetValue(row.Pair, out var target) ? target : null);
            Rows.Add(row);
        }
    }

    /// <summary>
    /// Resolves a click at scene coordinates (micrometers, Y-up) to a layer and
    /// selects it; a click on empty space clears the selection.
    /// </summary>
    /// <param name="xUm">Click X in micrometers.</param>
    /// <param name="yUm">Click Y in micrometers, Y-up.</param>
    /// <param name="textToleranceUm">Pick radius around text anchors in micrometers.</param>
    public void PickAt(double xUm, double yUm, double textToleranceUm)
    {
        SelectedRow = GdsLayerHitTester.TryPick(Scene, xUm, yUm, textToleranceUm, out var pair)
            ? Rows.FirstOrDefault(r => r.Pair == pair)
            : null;
    }

    /// <summary>Selects a layer from its legend row.</summary>
    [RelayCommand]
    private void SelectRow(GdsLayerPickerLayerRow row) => SelectedRow = row;

    /// <summary>Assigns the selected layer as waveguide (optical).</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AssignWaveguide() => Assign(GdsLayerFieldTarget.Waveguide);

    /// <summary>Assigns the selected layer as metal (electrical).</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AssignMetal() => Assign(GdsLayerFieldTarget.Metal);

    /// <summary>Assigns the selected layer as the port-label layer.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AssignPortLabels() => Assign(GdsLayerFieldTarget.PortLabels);

    /// <summary>Removes the selected layer from every layer field.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AssignIgnore() => Assign(null);

    private void Assign(GdsLayerFieldTarget? target)
    {
        if (SelectedRow is null)
            return;
        if (target is null)
            _assignments.Remove(SelectedRow.Pair);
        else
            _assignments[SelectedRow.Pair] = target.Value;
        SelectedRow.SetAssignment(target);
        _applyAssignment(SelectedRow.Pair, target);
    }

    partial void OnSelectedRowChanged(GdsLayerPickerLayerRow? value)
    {
        foreach (var row in Rows)
            row.IsSelected = ReferenceEquals(row, value);
    }

    /// <summary>Deterministic, well-separated per-layer color (golden-angle hue walk).</summary>
    private static Color PaletteColor(int index)
    {
        var hue = index * PaletteHueStepDegrees % 360.0;
        return HsvColor.ToRgb(hue, 0.65, 0.95);
    }
}
