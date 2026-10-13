using System.Collections.ObjectModel;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Analysis.LogicAnalysis.GateBuilding;
using CAP_Core.Components.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.MakeGate;

/// <summary>
/// The guided "Make gate" step in the right panel: (1) Pins — every external pin of
/// the selected group gets a suggested role chip and a short name; (2) Simulate — the
/// existing truth-table extraction runs and every row's raw output power shows as a
/// bar; (3) Threshold — suggested automatically from the intended function (or the
/// largest gap for a custom gate) with its margin in dB and an overlap warning, while
/// a manual override stays possible; (4) Save — renames the pins, persists the
/// assignment (which makes the group a gate) and optionally saves it to the library,
/// then offers to open the Logic tab. Non-modal: the canvas stays interactive.
/// </summary>
public partial class MakeGateViewModel : ObservableObject
{
    private readonly TruthTableExtractor _extractor = new();
    private readonly GatePinRoleSuggester _suggester = new();
    private readonly GateThresholdAdvisor _advisor = new();
    private ComponentGroup? _group;
    private DesignCanvasViewModel? _canvas;
    private CancellationTokenSource? _simulateCts;
    private TruthTable? _table;

    /// <summary>True while the Make-gate steps replace the plain truth table in the right panel.</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>True while exactly one group is selected (the "Make gate…" entry is offered).</summary>
    [ObservableProperty]
    private bool _canMakeGate;

    /// <summary>Name the gate (group) gets on save; also the library entry name.</summary>
    [ObservableProperty]
    private string _gateName = "";

    /// <summary>The intended logic function; drives the automatic threshold.</summary>
    [ObservableProperty]
    private GateFunctionOption _selectedFunction = GateFunctionOption.All[0];

    /// <summary>True while the truth-table extraction runs.</summary>
    [ObservableProperty]
    private bool _isProcessing;

    /// <summary>True once rows with raw powers are available.</summary>
    [ObservableProperty]
    private bool _hasResult;

    /// <summary>Status, hint or validation message of the panel.</summary>
    [ObservableProperty]
    private string _statusText = "";

    /// <summary>True once the current definition was saved as a gate.</summary>
    [ObservableProperty]
    private bool _isSaved;

    /// <summary>The group's external pins with their roles and short names.</summary>
    public ObservableCollection<GatePinRowViewModel> Pins { get; } = new();

    /// <summary>The intended-function picker entries.</summary>
    public IReadOnlyList<GateFunctionOption> Functions => GateFunctionOption.All;

    /// <summary>One row per input pattern and output with its raw power.</summary>
    public ObservableCollection<GateResultRowViewModel> Rows { get; } = new();

    /// <summary>Executes undoable commands (wired to the app's command manager); null runs them directly.</summary>
    public Action<IUndoableCommand>? ExecuteCommand { get; set; }

    /// <summary>Saves the group as a library prefab under the given name; returns false when unavailable.</summary>
    public Func<ComponentGroup, string, bool>? SaveToLibraryRequested { get; set; }

    /// <summary>Opens the analysis dock on the Logic tab.</summary>
    public Action? OpenLogicRequested { get; set; }

    /// <summary>Raised after a group was saved as a gate.</summary>
    public event EventHandler? GateSaved;

    /// <summary>
    /// Follows the canvas selection: exactly one selected group enables the entry; a
    /// different group (or none) closes the steps and cancels a running simulation.
    /// </summary>
    public void ConfigureForSelection(ComponentViewModel? component, DesignCanvasViewModel? canvas)
    {
        _canvas = canvas;
        var group = component?.Component as ComponentGroup;
        var singleSelection = canvas == null || canvas.Selection.SelectedComponents.Count <= 1;
        CanMakeGate = group != null && singleSelection;
        if (ReferenceEquals(group, _group) && CanMakeGate)
            return;

        _simulateCts?.Cancel();
        _group = CanMakeGate ? group : null;
        IsActive = false;
        ResetResult();
    }

    /// <summary>Opens the steps for the selected group, prefilled from a persisted assignment or the suggestions.</summary>
    [RelayCommand]
    private void Open()
    {
        if (!CanMakeGate || _group == null)
            return;
        ResetResult();
        LoadPins();
        GateName = DefaultGateName();
        StatusText = Translate("MakeGate.PinsHint");
        IsActive = true;
    }

    /// <summary>Closes the steps and returns to the plain truth table.</summary>
    [RelayCommand]
    private void Close()
    {
        _simulateCts?.Cancel();
        IsActive = false;
    }

    /// <summary>Rebuilds the pin rows from the group: persisted roles first, else the heuristic.</summary>
    private void LoadPins()
    {
        Pins.Clear();
        if (_group == null)
            return;
        var saved = _group.TruthTablePinAssignment;
        var rows = _suggester.Suggest(_group, ExternallyConnectedPins()).Select(suggestion =>
        {
            var role = saved == null ? suggestion.Role : PersistedRole(saved, suggestion.PinName);
            var name = saved == null ? suggestion.SuggestedName : suggestion.PinName;
            return new GatePinRowViewModel(suggestion.PinName, role, name, suggestion.Role);
        });
        // The gate interface first (inputs in bit order, outputs, bias), unused pins last.
        foreach (var row in rows.OrderBy(r => (int)r.Role).ThenBy(r => BitOrder(saved, r)))
            Pins.Add(row);
        if (saved != null)
            Threshold = saved.Threshold;
    }

    /// <summary>
    /// Sort key inside a role: the persisted input bit order when the group already is a
    /// gate, else the suggested short name (A before B) — the list order is the bit order.
    /// </summary>
    private static string BitOrder(TruthTablePinAssignment? saved, GatePinRowViewModel row)
    {
        var savedIndex = saved?.InputPinNames.IndexOf(row.PinName) ?? -1;
        return savedIndex >= 0 ? savedIndex.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) : row.Name;
    }

    private static GatePinRole PersistedRole(TruthTablePinAssignment saved, string pinName)
    {
        if (saved.InputPinNames.Contains(pinName)) return GatePinRole.Input;
        if (saved.OutputPinNames.Contains(pinName)) return GatePinRole.Output;
        if (saved.BiasPinNames.Contains(pinName)) return GatePinRole.Bias;
        return GatePinRole.Unused;
    }

    /// <summary>Names of the group's external pins wired to something on the canvas.</summary>
    private IReadOnlySet<string> ExternallyConnectedPins()
    {
        var connected = new HashSet<string>();
        if (_canvas == null || _group == null)
            return connected;
        var wiredPins = _canvas.Connections
            .SelectMany(c => new[] { c.Connection.StartPin, c.Connection.EndPin })
            .ToHashSet();
        foreach (var pin in _group.ExternalPins.Where(p => wiredPins.Contains(p.InternalPin)))
            connected.Add(pin.Name);
        return connected;
    }

    /// <summary>The group's own name unless it is an auto-generated placeholder.</summary>
    private string DefaultGateName()
    {
        if (_group == null)
            return "";
        return _group.GroupName.StartsWith(AutoGroupNamePrefix, StringComparison.Ordinal)
            ? UniqueGateName(SelectedFunction.GateNameStem ?? Translate("MakeGate.DefaultGateName"))
            : _group.GroupName;
    }

    /// <summary>Prefix of the timestamped names CreateGroupCommand gives new groups.</summary>
    private const string AutoGroupNamePrefix = "Group_";

    /// <summary>Appends a number when another group on the canvas already carries the name.</summary>
    private string UniqueGateName(string stem)
    {
        var taken = _canvas?.Components
            .Select(c => c.Component)
            .OfType<ComponentGroup>()
            .Where(g => !ReferenceEquals(g, _group))
            .Select(g => g.GroupName)
            .ToHashSet() ?? new HashSet<string>();
        if (!taken.Contains(stem))
            return stem;
        var number = 2;
        while (taken.Contains($"{stem}{number}"))
            number++;
        return $"{stem}{number}";
    }

    private void ResetResult()
    {
        _table = null;
        Rows.Clear();
        HasResult = false;
        IsSaved = false;
        StatusText = "";
        ClearThresholdSuggestion();
    }

    private static string Translate(string key) => LocalizationService.Instance.Translate(key);
}
