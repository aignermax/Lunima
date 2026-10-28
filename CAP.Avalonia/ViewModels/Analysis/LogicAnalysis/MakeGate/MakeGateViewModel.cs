using System.Collections.ObjectModel;
using System.ComponentModel;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Analysis.LogicAnalysis.GateBuilding;
using CAP_Core.Components.Core;
using CAP_Core.Analysis.Hierarchy;
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
public partial class MakeGateViewModel : ObservableObject, IAsyncDisposable
{
    private readonly TruthTableExtractor _extractor;
    private readonly GatePinRoleSuggester _suggester = new();
    private readonly GateThresholdAdvisor _advisor = new();
    private ComponentGroup? _group;

    /// <summary>The exact group whose current editor state is being displayed.</summary>
    internal ComponentGroup? ConfiguredGroup => _group;
    private DesignCanvasViewModel? _canvas;
    private readonly MakeGateWorkSession _session = new();
    private readonly ObservablePublicationQueue _publication = new();
    private Task? _retirement;
    private TruthTable? _table;

    /// <summary>Creates the editor with its optical extraction worker.</summary>
    public MakeGateViewModel(TruthTableExtractor? extractor = null) => _extractor = extractor ?? new TruthTableExtractor();

    /// <summary>Retires editor work before its owning shell is disposed.</summary>
    public Task StopAsync()
    {
        if (_retirement != null) return _retirement;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _retirement = completion.Task;
        _ = RetireAsync(completion);
        return _retirement;
    }
    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task RetireAsync(TaskCompletionSource completion)
    {
        try
        {
            var work = _session.StopAsync();
            var version = _session.Version;
            DetachPins();
            try
            {
                await _publication.PublishAsync(() =>
                {
                    IsActive = false;
                    CanMakeGate = false;
                    ResetResultDisplay(version);
                });
            }
            finally
            {
                try { await work; }
                finally
                {
                    _group = null;
                    _canvas = null;
                    ExecuteCommand = null;
                    SaveToLibraryRequested = null;
                    OpenLogicRequested = null;
                    GateSaved = null;
                    GateDefinitionChanged = null;
                    await _publication.PublishAsync(() => Pins.Clear());
                }
            }
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }

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

    /// <summary>Raised when a saved definition is applied, undone, or redone.</summary>
    public event EventHandler? GateDefinitionChanged;

    /// <summary>
    /// Follows the canvas selection: exactly one selected group enables the entry; a
    /// different group (or none) closes the steps and cancels a running simulation.
    /// </summary>
    public void ConfigureForSelection(ComponentViewModel? component, DesignCanvasViewModel? canvas)
    {
        if (_session.IsStopping) return;
        var group = component?.Component as ComponentGroup;
        var singleSelection = canvas == null || canvas.Selection.SelectedComponents.Count <= 1;
        var canMakeGate = group is { IsDie: false } && singleSelection;
        if (ReferenceEquals(group, _group) && ReferenceEquals(canvas, _canvas)
            && canMakeGate && _session.IsConfiguredCurrent)
        {
            var currentVersion = _session.Version;
            _publication.Publish(() => { if (_session.IsCurrent(currentVersion)) CanMakeGate = true; });
            return;
        }

        // Revoke private ownership before any observable notification or deferred display work.
        _canvas = canvas;
        _group = canMakeGate ? group : null;
        _session.Configure(canvas?.Document, () => _group != null && ReferenceEquals(_group, group)
            && (canvas == null || DesignHierarchy.From(canvas.Groups.RootComponents.Select(view => view.Component))
                .Groups.Any(value => ReferenceEquals(value, group))));
        var version = _session.Version;
        _table = null;
        _publication.Publish(() =>
        {
            if (_session.Version != version || _session.IsStopping) return;
            CanMakeGate = canMakeGate;
            if (_session.Version != version || _session.IsStopping) return;
            IsActive = false;
            if (_session.Version == version && !_session.IsStopping) ResetResultDisplay(version);
        });
    }

    /// <summary>Opens the steps for the selected group, prefilled from a persisted assignment or the suggestions.</summary>
    [RelayCommand]
    private void Open()
    {
        if (_group == null || !_session.IsConfiguredCurrent) return;
        _session.Invalidate();
        var version = _session.Version;
        _table = null;
        _publication.Publish(() =>
        {
            if (!_session.IsCurrent(version)) return;
            CanMakeGate = true;
            if (!_session.IsCurrent(version)) return;
            ResetResultDisplay(version);
            if (!_session.IsCurrent(version) || !LoadPins(version)) return;
            GateName = DefaultGateName();
            if (!_session.IsCurrent(version)) return;
            StatusText = Translate("MakeGate.PinsHint");
            if (_session.IsCurrent(version)) IsActive = true;
        });
    }

    /// <summary>Closes the steps and returns to the plain truth table.</summary>
    [RelayCommand]
    private void Close()
    {
        _session.Invalidate();
        var version = _session.Version;
        _publication.Publish(() => { if (_session.Version == version) IsActive = false; });
    }

    /// <summary>Rebuilds the pin rows from the group: persisted roles first, else the heuristic.</summary>
    private bool LoadPins(long version)
    {
        var group = _group;
        if (group == null || !_session.IsCurrent(version)) return false;
        DetachPins();
        Pins.Clear();
        if (!_session.IsCurrent(version) || !ReferenceEquals(_group, group)) return false;
        var saved = group.TruthTablePinAssignment;
        var rows = _suggester.Suggest(group, ExternallyConnectedPins()).Select(suggestion =>
        {
            var role = saved == null ? suggestion.Role : PersistedRole(saved, suggestion.PinName);
            var name = saved == null ? suggestion.SuggestedName : suggestion.PinName;
            return new GatePinRowViewModel(suggestion.PinName, role, name, suggestion.Role,
                group.ExternalPins.Single(p => p.Name == suggestion.PinName));
        });
        // The gate interface first (inputs in bit order, outputs, bias), unused pins last.
        foreach (var row in rows.OrderBy(r => (int)r.Role).ThenBy(r => BitOrder(saved, r)))
        {
            if (!_session.IsCurrent(version) || !ReferenceEquals(_group, group)) return false;
            row.PropertyChanged += PinEditorChanged;
            Pins.Add(row);
        }
        if (!_session.IsCurrent(version) || !ReferenceEquals(_group, group)) return false;
        if (saved != null)
            Threshold = saved.Threshold;
        return _session.IsCurrent(version) && ReferenceEquals(_group, group);
    }

    private void DetachPins()
    {
        foreach (var pin in Pins) pin.PropertyChanged -= PinEditorChanged;
    }

    private void PinEditorChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName is not (nameof(GatePinRowViewModel.Role) or nameof(GatePinRowViewModel.Name))) return;
        if (!IsProcessing && change.PropertyName == nameof(GatePinRowViewModel.Name)) return;
        _session.Invalidate();
        ResetResult();
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
        var version = _session.Version;
        _publication.Publish(() => ResetResultDisplay(version));
    }

    private void ResetResultDisplay(long version)
    {
        if (_session.Version != version) return;
        _table = null;
        Rows.Clear();
        if (_session.Version != version) return;
        HasResult = false;
        if (_session.Version != version) return;
        IsSaved = false;
        if (_session.Version != version) return;
        StatusText = "";
        if (_session.Version == version) ClearThresholdSuggestion(() => _session.Version == version);
    }

    private static string Translate(string key) => LocalizationService.Instance.Translate(key);
}
