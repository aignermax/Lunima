using System.Collections.ObjectModel;
using CAP.Avalonia.ViewModels.Panels.Interaction;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Creation;
using CAP_Core.Components.PinKinds;
using CAP_Core.Components.Process;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.Services;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// ViewModel for canvas interaction logic.
/// Owns interaction mode and selection commands; focused collaborators own placement, connection and drag gestures.
/// </summary>
public partial class CanvasInteractionViewModel : ObservableObject, IDisposable
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly CommandManager _commandManager;

    [ObservableProperty]
    private InteractionMode _currentMode = InteractionMode.Select;

    /// <summary>
    /// Short, mode-relevant shortcut hint shown in the status bar (#1162); the full
    /// shortcut reference lives in the (?) flyout next to it.
    /// </summary>
    [ObservableProperty]
    private string _modeShortcutHints = "";

    [ObservableProperty]
    private ComponentTemplate? _selectedTemplate;

    [ObservableProperty]
    private GroupTemplate? _selectedGroupTemplate;

    [ObservableProperty]
    private ComponentViewModel? _selectedComponent;

    [ObservableProperty]
    private WaveguideConnectionViewModel? _selectedWaveguideConnection;

    /// <summary>
    /// Canvas-level pin-less frozen path currently selected (issue #856). Mutually
    /// exclusive with component and connection selection, like
    /// <see cref="SelectedWaveguideConnection"/>.
    /// </summary>
    [ObservableProperty]
    private CanvasFrozenPathViewModel? _selectedCanvasFrozenPath;

    partial void OnSelectedCanvasFrozenPathChanged(
        CanvasFrozenPathViewModel? oldValue, CanvasFrozenPathViewModel? newValue)
    {
        if (newValue != null)
        {
            newValue.IsSelected = true;
            if (!_canvas.Selection.SelectedFrozenPaths.Contains(newValue))
                _canvas.Selection.SelectedFrozenPaths.Add(newValue);
        }
        if (oldValue != null)
        {
            oldValue.IsSelected = false;
            _canvas.Selection.SelectedFrozenPaths.Remove(oldValue);
        }
    }

    /// <summary>
    /// True when a connection is selected — directly or via the rubber-band selection
    /// (issue #862). Metal traces route with curved bends like waveguides (#854), so
    /// routing styles and bend handles apply to electrical connections too — the
    /// routing panel binds its visibility to this.
    /// </summary>
    public bool IsConnectionSelected =>
        SelectedWaveguideConnection is not null ||
        _canvas.Selection.SelectedConnections.Any();

    partial void OnSelectedWaveguideConnectionChanged(WaveguideConnectionViewModel? value) =>
        OnPropertyChanged(nameof(IsConnectionSelected));

    private readonly CanvasConnectionGesture _connectionGesture;
    private readonly CanvasPlacementActions _placementActions;
    private readonly CanvasMoveGesture _moveGesture;
    private readonly GroupLibraryActions _groupLibraryActions;
    private readonly CanvasDieInteractionRules _dieRules;
    private readonly CanvasGroupingActions _grouping;

    public bool IsDieSelected => IsDie(SelectedComponent);
    public bool SelectionHasDie => _dieRules.SelectionHasDie(SelectedComponent);
    public string? GroupBlockedReason => _dieRules.GroupBlockedReason(SelectedComponent);
    public static bool IsDie(ComponentViewModel? item) => CanvasDieInteractionRules.IsDie(item);

    private void RefreshDieRules()
    {
        OnPropertyChanged(nameof(IsDieSelected));
        OnPropertyChanged(nameof(SelectionHasDie));
        OnPropertyChanged(nameof(GroupBlockedReason));
        CreateGroupCommand.NotifyCanExecuteChanged();
        UngroupCommand.NotifyCanExecuteChanged();
        RenameGroupCommand.NotifyCanExecuteChanged();
        OpenSelectedComponentSettingsCommand.NotifyCanExecuteChanged();
    }

    public void CanvasMouseMove(double canvasX, double canvasY) =>
        _connectionGesture.MouseMove(CurrentMode == InteractionMode.Connect, canvasX, canvasY);
    public void StartMoveComponent(ComponentViewModel component) => _moveGesture.StartMoveComponent(component);
    public void StartGroupMove(IEnumerable<ComponentViewModel> components) => _moveGesture.StartGroupMove(components);
    public void EndMoveComponent() => _moveGesture.EndMoveComponent();
    public void EndGroupMove(IEnumerable<ComponentViewModel> components) => _moveGesture.EndGroupMove(components);
    /// <summary>
    /// Callback to update status text in the UI.
    /// </summary>
    public Action<string>? UpdateStatus { get; set; }

    /// <summary>
    /// Callback to notify when selection changes (for syncing with hierarchy panel).
    /// </summary>
    public Action<ComponentViewModel?>? OnSelectionChanged { get; set; }

    /// <summary>
    /// Callback to clear group template selection in left panel.
    /// </summary>
    public Action? ClearLeftPanelGroupSelection { get; set; }

    /// <summary>
    /// Callback to clear component template selection in main view.
    /// </summary>
    public Action? ClearComponentTemplateSelection { get; set; }

    /// <summary>
    /// Callback for "Edit Component…" from the canvas context menu. Wired by
    /// <c>MainWindow.axaml.cs</c> to the unified "Edit Component" editor when the component's
    /// PDK template is resolvable and editable, falling back to the per-instance
    /// <c>ComponentSettingsDialog</c> otherwise (ComponentGroups, template-less instances).
    /// </summary>
    public Action<ComponentViewModel>? OpenComponentSettings { get; set; }

    /// <summary>
    /// Callback invoked when the user probes an element in Probe mode (issue #691):
    /// carries the classified probe target plus the click position in canvas coordinates.
    /// Wired by <c>MainViewModel</c> to open the mode-slice flyout at the click point.
    /// </summary>
    public Action<CAP_Core.Solvers.ModeProbe.ProbeTarget, double, double>? ProbeRequested { get; set; }

    /// <summary>
    /// Shared placement-policy context (issues #570/#653/#737), consulted before placement
    /// and paste so a component from a foreign PDK is rejected. Wired by <c>MainViewModel</c>
    /// to the same instance the AI placement path uses, so manual and AI placement can never
    /// apply diverging rules. Defaults to <see cref="PlacementPolicyContext.Unrestricted"/>.
    /// </summary>
    public PlacementPolicyContext PlacementContext
    {
        get => _placementActions.PlacementContext;
        set => _placementActions.PlacementContext = value;
    }

    /// <summary>
    /// The active workspace's placement rule for library components: the hint to show when the
    /// workspace does not place the template, null when it does. Consulted when a template is
    /// armed and again at every placement (click, drag &amp; drop), so no path bypasses it.
    /// Wired by <c>MainViewModel</c>; unwired, every template is placeable.
    /// </summary>
    public Func<ComponentTemplate, string?>? ComponentPlacementRefusal
    {
        get => _placementActions.ComponentPlacementRefusal;
        set => _placementActions.ComponentPlacementRefusal = value;
    }

    /// <summary>The group-template twin of <see cref="ComponentPlacementRefusal"/> (saved groups, PDK macros, gates).</summary>
    public Func<GroupTemplate, string?>? GroupPlacementRefusal
    {
        get => _placementActions.GroupPlacementRefusal;
        set => _placementActions.GroupPlacementRefusal = value;
    }

    /// <summary>
    /// Callback returning the minimum allowed waveguide bend radius (µm) of the design's active
    /// fabrication process, consulted by the in-canvas bend-handle drag so an edit cannot shrink
    /// a bend below what the process permits. Wired by <c>MainViewModel</c> to
    /// <c>WaveguideBendRadiusResolver.Resolve</c>; when unwired (or the process is unresolvable)
    /// the drag falls back to <c>BendRadiusEditor.MinRadiusMicrometers</c>.
    /// </summary>
    public Func<double>? GetMinBendRadiusMicrometers { get; set; }

    /// <summary>
    /// Callback returning the minimum allowed METAL bend radius (µm) of the design's active
    /// fabrication process (issue #854), consulted by the bend-handle drag on electrical
    /// connections. Wired by <c>MainViewModel</c> to the metal routing spec provider; when
    /// unwired the drag falls back to <c>BendRadiusEditor.MinRadiusMicrometers</c>.
    /// </summary>
    public Func<double>? GetMetalMinBendRadiusMicrometers { get; set; }

    public CanvasInteractionViewModel(
        DesignCanvasViewModel canvas,
        CommandManager commandManager,
        ComponentLibraryViewModel? libraryViewModel = null,
        GroupPreviewGenerator? previewGenerator = null,
        IInputDialogService? inputDialogService = null,
        ErrorConsoleService? errorConsole = null)
    {
        _canvas = canvas;
        _commandManager = commandManager;
        _connectionGesture = new(canvas, commandManager, message => UpdateStatus?.Invoke(message));
        _placementActions = new(canvas, commandManager, libraryViewModel, errorConsole, message => UpdateStatus?.Invoke(message));
        _moveGesture = new(canvas, commandManager);
        _dieRules = new(canvas);
        _grouping = new(canvas, commandManager, _dieRules, libraryViewModel != null, message => UpdateStatus?.Invoke(message));
        _groupLibraryActions = new(commandManager, libraryViewModel, previewGenerator, inputDialogService,
            message => UpdateStatus?.Invoke(message));

        // Hierarchy → right panel: when canvas.SelectedComponent changes externally
        // (e.g. from the hierarchy panel), mirror it so the right-panel property editor updates.
        _canvas.PropertyChanged += OnCanvasPropertyChanged;

        // Rubber-band connection selection (issue #862) feeds the routing panel's visibility.
        _canvas.Selection.SelectedConnections.CollectionChanged += OnSelectedConnectionsChanged;
        _canvas.Selection.SelectedFrozenPaths.CollectionChanged += OnSelectedFrozenPathsChanged;

        RefreshModeShortcutHints();
        _canvas.Selection.SelectedComponents.CollectionChanged += OnSelectedComponentsChanged;
    }

    private void OnSelectedComponentsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RefreshDieRules();
    private void OnSelectedConnectionsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => OnPropertyChanged(nameof(IsConnectionSelected));
    private void OnSelectedFrozenPathsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (SelectedCanvasFrozenPath is { } selected && !_canvas.Selection.SelectedFrozenPaths.Contains(selected))
            SelectedCanvasFrozenPath = null;
    }

    private bool _disposed;

    /// <summary>Detaches selection synchronization when this interaction owner is retired.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _canvas.PropertyChanged -= OnCanvasPropertyChanged;
        _canvas.Selection.SelectedComponents.CollectionChanged -= OnSelectedComponentsChanged;
        _canvas.Selection.SelectedConnections.CollectionChanged -= OnSelectedConnectionsChanged;
        _canvas.Selection.SelectedFrozenPaths.CollectionChanged -= OnSelectedFrozenPathsChanged;
        _connectionGesture.Reset();
        _canvas.ClearPinHighlight();
        _moveGesture.Reset();
    }

    /// <summary>
    /// Recomputes <see cref="ModeShortcutHints"/> from the current mode — invoked on every
    /// mode change and, via <c>MainViewModel</c>, on a live UI-language switch so the
    /// status-bar hint stays in the active language.
    /// </summary>
    public void RefreshModeShortcutHints() =>
        ModeShortcutHints = Services.Localization.LocalizationService.Instance
            .Translate(StatusBarShortcutHints.KeyForMode(CurrentMode));

    /// <summary>
    /// Keeps <see cref="SelectedComponent"/> in sync when
    /// <see cref="DesignCanvasViewModel.SelectedComponent"/> is changed externally
    /// (e.g. by the hierarchy panel).
    /// CommunityToolkit's equality check prevents the setter from firing again when
    /// the value is already up-to-date, so there is no feedback loop.
    /// </summary>
    private void OnCanvasPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesignCanvasViewModel.SelectedComponent))
            SelectedComponent = _canvas.SelectedComponent;
    }

    partial void OnSelectedTemplateChanged(ComponentTemplate? value)
    {
        if (value != null && IsRefused(ComponentPlacementRefusal?.Invoke(value)))
        {
            SelectedTemplate = null;
            return;
        }
        if (value != null)
        {
            CurrentMode = InteractionMode.PlaceComponent;
            SelectedGroupTemplate = null; // Deselect group template in CanvasInteraction
            ClearLeftPanelGroupSelection?.Invoke(); // Deselect group template in LeftPanel UI
            UpdateStatus?.Invoke($"Click on canvas to place: {value.Name}");
        }
    }

    partial void OnSelectedGroupTemplateChanged(GroupTemplate? value)
    {
        if (value != null && IsRefused(GroupPlacementRefusal?.Invoke(value)))
        {
            SelectedGroupTemplate = null;
            ClearLeftPanelGroupSelection?.Invoke();
            return;
        }
        if (value != null)
        {
            CurrentMode = InteractionMode.PlaceGroupTemplate;
            SelectedTemplate = null; // Deselect component template in CanvasInteraction
            ClearComponentTemplateSelection?.Invoke(); // Deselect component template in UI ListBox
            UpdateStatus?.Invoke($"Click on canvas to place group: {value.Name}");
        }
    }

    partial void OnCurrentModeChanged(InteractionMode value)
    {
        _connectionGesture.Reset();
        _canvas.ClearPinHighlight();

        // Deselect templates when switching away from placement modes
        if (value != InteractionMode.PlaceComponent && value != InteractionMode.PlaceGroupTemplate)
        {
            SelectedTemplate = null;
            SelectedGroupTemplate = null;
            // Clear UI selections as well
            ClearComponentTemplateSelection?.Invoke();
            ClearLeftPanelGroupSelection?.Invoke();
        }

        // Deselect canvas components when switching away from Select mode
        if (value != InteractionMode.Select)
        {
            SelectedComponent = null;
        }

        var statusText = value switch
        {
            InteractionMode.Select => "Select mode: Click to select, drag to move",
            InteractionMode.PlaceComponent when SelectedTemplate != null => $"Place mode: Click to place {SelectedTemplate.Name}",
            InteractionMode.PlaceComponent => "Place mode: Select a component from the library",
            InteractionMode.PlaceGroupTemplate when SelectedGroupTemplate != null => $"Place mode: Click to place group {SelectedGroupTemplate.Name}",
            InteractionMode.PlaceGroupTemplate => "Place mode: Select a group from Saved Groups",
            InteractionMode.Connect => "Connect mode: Move near a pin to start connection",
            InteractionMode.Delete => "Delete mode: Click on component or connection to delete",
            InteractionMode.Probe => "Probe mode: Click a waveguide or coupler to inspect its mode slice",
            InteractionMode.PickAnalysisOutput =>
                Services.Localization.LocalizationService.Instance.Translate("Analysis.Output.PickPrompt"),
            InteractionMode.Cut =>
                Services.Localization.LocalizationService.Instance.Translate("Status.CutModePrompt"),
            _ => "Ready"
        };

        UpdateStatus?.Invoke(statusText);
        RefreshModeShortcutHints();
    }

    partial void OnSelectedComponentChanged(ComponentViewModel? value)
    {
        // Keep canvas in sync when this property is set from outside (e.g. tests or mirroring).
        if (_canvas.SelectedComponent != value)
            _canvas.SelectedComponent = value;

        if (value?.IsLightSource == true)
        {
            var cfg = value.LaserConfig!;
            UpdateStatus?.Invoke($"Selected: {value.Name} [{cfg.WavelengthLabel}, Power={cfg.InputPower:F2}]");
        }

        OnSelectionChanged?.Invoke(value);
        RefreshDieRules();
        OpenSelectedComponentSettingsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Handles canvas click events.
    /// </summary>
    public void CanvasClicked(double canvasX, double canvasY)
    {
        switch (CurrentMode)
        {
            case InteractionMode.PlaceComponent:
                PlaceComponentAt(canvasX, canvasY);
                break;
            case InteractionMode.PlaceGroupTemplate:
                PlaceGroupTemplateAt(canvasX, canvasY);
                break;
            case InteractionMode.Select:
                SelectAt(canvasX, canvasY);
                break;
            case InteractionMode.Connect:
                var pin = _canvas.HighlightedPin?.Pin ?? _canvas.GetPinAt(canvasX, canvasY);
                if (pin != null)
                {
                    _connectionGesture.PinClicked(pin);
                }
                else
                {
                    CurrentMode = InteractionMode.Select;
                    _canvas.ClearPinHighlight();
                }
                break;
            case InteractionMode.Delete:
                DeleteAt(canvasX, canvasY);
                break;
            case InteractionMode.Probe:
                ProbeAt(canvasX, canvasY);
                break;
            case InteractionMode.PickAnalysisOutput:
                PickAnalysisOutputAt(canvasX, canvasY);
                break;
        }
    }

    /// <summary>
    /// Designates the coupler at the given canvas position as THE analysis output
    /// (#754). A coupler whose laser is still on is switched off first (explicit user
    /// intent: the output listens). Clicking anything else keeps the picker active
    /// with a status hint; a successful pick returns to Select mode.
    /// </summary>
    private void PickAnalysisOutputAt(double x, double y)
    {
        var loc = Services.Localization.LocalizationService.Instance;
        var component = ComponentAt(x, y);
        if (component?.IsLightSource != true)
        {
            UpdateStatus?.Invoke(loc.Translate("Analysis.Output.PickNotACoupler"));
            return;
        }

        bool laserWasOn = component.LaserConfig!.IsEnabled;
        // ONE composite undoable command: designation + laser-off together, so a single
        // Ctrl+Z reverts the whole pick (#762 review, finding [2]).
        _commandManager.ExecuteCommand(new PickAnalysisOutputCommand(component, _canvas.AnalysisOutput));
        var messageKey = laserWasOn ? "Analysis.Output.DesignatedLaserOff" : "Analysis.Output.Designated";
        UpdateStatus?.Invoke(string.Format(loc.Translate(messageKey), component.Name));
        CurrentMode = InteractionMode.Select;
    }

    /// <summary>
    /// Handles pin click events in Connect mode.
    /// </summary>
    public void PinClicked(PhysicalPin pin)
    {
        if (CurrentMode == InteractionMode.Connect)
        {
            _connectionGesture.PinClicked(pin);
        }
    }

    private void PlaceGroupTemplateAt(double x, double y) => _placementActions.PlaceGroup(SelectedGroupTemplate, x, y);

    private void PlaceComponentAt(double x, double y) => _placementActions.PlaceComponent(SelectedTemplate, x, y);

    /// <summary>Shows a workspace refusal hint; true when there was one (the placement is refused).</summary>
    private bool IsRefused(string? refusal)
    {
        if (refusal == null)
            return false;
        UpdateStatus?.Invoke(refusal);
        return true;
    }

    /// <summary>
    /// True drag-and-drop placement from the library (issue #1157): the dragged template is
    /// placed at the release point and the canvas returns to Select mode — drag&amp;drop is a
    /// one-shot gesture, unlike click-to-place which stays armed for repeated placement.
    /// The mode switch happens BEFORE the placement so the outcome ("Placed …" or the
    /// rejection reason) is the last status the user sees, not the Select-mode prompt.
    /// </summary>
    public void DropComponentTemplateAt(ComponentTemplate template, double canvasX, double canvasY)
    {
        CurrentMode = InteractionMode.Select;
        _placementActions.PlaceComponent(template, canvasX, canvasY);
    }

    /// <summary>
    /// True drag-and-drop placement for a saved group (issue #1157) — the group-twin of
    /// <see cref="DropComponentTemplateAt"/>.
    /// </summary>
    public void DropGroupTemplateAt(GroupTemplate template, double canvasX, double canvasY)
    {
        CurrentMode = InteractionMode.Select;
        _placementActions.PlaceGroup(template, canvasX, canvasY);
    }

    /// <summary>
    /// Selects the component or connection at the given canvas position, keeping the
    /// <see cref="DesignCanvasViewModel.Selection"/> set and <see cref="SelectedComponent"/> in sync.
    /// Invoked by the canvas right-click handler so the context menu acts on the element under the
    /// cursor rather than the previously selected one.
    /// </summary>
    public void SelectComponentAt(double canvasX, double canvasY)
    {
        var hit = ComponentAt(canvasX, canvasY);

        // Right-clicking one of several already-selected components keeps the whole
        // multi-selection (so "Create Group" stays available) and just makes the
        // clicked one the primary for the context menu / component settings.
        // Right-clicking outside the selection (or with only one selected) selects
        // just that component, exactly like a left-click would.
        if (hit != null && _canvas.Selection.HasMultipleSelected && _canvas.Selection.SelectedComponents.Contains(hit))
        {
            SelectedComponent = hit;
            _canvas.SelectedComponent = hit;
            SelectedWaveguideConnection = null;
            UpdateStatus?.Invoke($"Selected: {hit.Name} ({_canvas.Selection.SelectedComponents.Count} selected)");
            return;
        }

        SelectAt(canvasX, canvasY);
    }

    /// <summary>
    /// Returns the topmost regular component whose bounds contain the point, or null.
    /// Background geometry (die frames, logos) is skipped: its box covers whatever lies
    /// on top of it, so it only wins when nothing else is under the cursor
    /// (<see cref="BackgroundComponentAt"/>).
    /// </summary>
    private ComponentViewModel? ComponentAt(double x, double y) =>
        _canvas.InteractiveComponents
            .Where(c => c.Component.IsRoutingObstacle && Contains(c, x, y))
            .LastOrDefault();

    /// <summary>Returns the topmost background component whose bounds contain the point, or null.</summary>
    private ComponentViewModel? BackgroundComponentAt(double x, double y) =>
        _canvas.Components
            .Where(c => !c.Component.IsRoutingObstacle && Contains(c, x, y))
            .LastOrDefault();

    private bool Contains(ComponentViewModel c, double x, double y) =>
        x >= c.X && x <= c.X + c.Width && y >= c.Y && y <= c.Y + c.Height
        && (!c.Component.IsRoutingObstacle
            || Controls.OutlineHitTester.Hits(c.Component, new global::Avalonia.Point(x, y),
                Controls.OutlineHitTester.ToleranceAt(_canvas.ViewZoom)));

    /// <summary>Activates the work surface under an explicit selection click, independently of object selection.</summary>
    public Action<double, double>? SelectionPointChanged { get; set; }

    private void SelectAt(double x, double y)
    {
        // Deselect all (parts on dies included)
        foreach (var comp in _canvas.Components.Concat(_canvas.InteractiveComponents))
        {
            comp.IsSelected = false;
        }
        foreach (var conn in _canvas.Connections)
        {
            conn.IsSelected = false;
        }
        // Empty the batch connection set BEFORE the click result is applied: the sync below
        // calls ClearSelection(), which would otherwise deselect a just-clicked batch member.
        _canvas.Selection.ClearConnectionSelection();

        // Find component at position (regular components first; background last, below).
        var component = ComponentAt(x, y);
        var connectionHit = component is null ? FindConnectionAt(x, y) : null;
        var frozenHit = component is null && connectionHit is null ? FindCanvasFrozenPathAt(x, y) : null;
        if (component is null && connectionHit is null && frozenHit is null)
            component = BackgroundComponentAt(x, y) ?? _dieRules.ChipDieAt(x, y);

        if (component != null)
        {
            component.IsSelected = true;
            SelectedComponent = component;
            _canvas.SelectedComponent = component;
            SelectedWaveguideConnection = null;
            SelectedCanvasFrozenPath = null;
            UpdateStatus?.Invoke($"Selected: {component.Name}");
        }
        else
        {
            var connection = connectionHit;
            if (connection != null)
            {
                connection.IsSelected = true;
                SelectedWaveguideConnection = connection;
                SelectedComponent = null;
                _canvas.SelectedComponent = null;
                SelectedCanvasFrozenPath = null;
                UpdateStatus?.Invoke($"Selected connection: {connection.PathLength:F1}µm, Loss: {connection.LossDb:F2}dB");
            }
            else if (frozenHit is { } frozenPath)
            {
                SelectedComponent = null;
                _canvas.SelectedComponent = null;
                SelectedWaveguideConnection = null;
                UpdateStatus?.Invoke(string.Format(
                    Services.Localization.LocalizationService.Instance.Translate("Status.FrozenPathSelected"),
                    frozenPath.Path.Path.TotalLengthMicrometers.ToString("F1")));
            }
            else
            {
                SelectedComponent = null;
                _canvas.SelectedComponent = null;
                SelectedWaveguideConnection = null;
                SelectedCanvasFrozenPath = null;
            }
        }

        SyncSelectionSetToClickResult();
        // Assign after click synchronization clears the previous batch selection.
        if (frozenHit != null) SelectedCanvasFrozenPath = frozenHit;
        SelectionPointChanged?.Invoke(x, y);
    }

    /// <summary>
    /// Mirrors the click result into the <see cref="DesignCanvasViewModel.Selection"/>
    /// set — the single sync point for every <see cref="SelectAt"/> caller (round 4
    /// review findings [0], [8]). An empty-canvas or connection click clears the set,
    /// otherwise the hierarchy panel re-mirrors the stale set and keeps its
    /// multi-highlight while the canvas looks deselected (field bug, round 4 final).
    /// Clicking a component OUTSIDE the set selects it alone; clicking a MEMBER keeps
    /// the whole set — the drag recognizer clicks through before deciding between
    /// group and single move, and a Ctrl+click release routes through here too, so
    /// collapsing would break group drag and Ctrl+click accumulation.
    /// </summary>
    private void SyncSelectionSetToClickResult()
    {
        var selection = _canvas.Selection;
        if (SelectedComponent == null)
        {
            selection.ClearSelection();
            return;
        }
        if (!selection.SelectedComponents.Contains(SelectedComponent))
        {
            selection.SelectSingle(SelectedComponent);
            return;
        }
        // Keep the set: SelectAt's deselect-all pass cleared the members' visual flag.
        foreach (var member in selection.SelectedComponents)
            member.IsSelected = true;
    }

    private void DeleteAt(double x, double y)
    {
        var component = _canvas.InteractiveComponents
            .Where(c => x >= c.X && x <= c.X + c.Width && y >= c.Y && y <= c.Y + c.Height)
            .LastOrDefault();

        if (component != null)
        {
            var name = component.Name;
            var cmd = _dieRules.OnDieSurface(new[] { component }, () => new DeleteComponentCommand(_canvas, component), "Delete");
            _commandManager.ExecuteCommand(cmd);
            SelectedComponent = null;
            UpdateStatus?.Invoke($"Deleted: {name}");
            return;
        }

        var connection = FindConnectionAt(x, y);
        if (connection != null)
        {
            var cmd = new DeleteConnectionCommand(_canvas, connection);
            _commandManager.ExecuteCommand(cmd);
            UpdateStatus?.Invoke("Deleted connection");
            return;
        }

        if (FindCanvasFrozenPathAt(x, y) is { } frozenPath)
        {
            if (SelectedCanvasFrozenPath == frozenPath)
                SelectedCanvasFrozenPath = null;
            _commandManager.ExecuteCommand(new DeleteCanvasFrozenPathCommand(_canvas, frozenPath));
            UpdateStatus?.Invoke(
                Services.Localization.LocalizationService.Instance.Translate("Status.FrozenPathDeleted"));
        }
    }

    /// <summary>
    /// Probes the element at the given canvas position (issue #691): a clicked waveguide
    /// connection carries its own width; a clicked component is classified as fiber
    /// coupler / interference region and borrows the width of an attached connection.
    /// Raises <see cref="ProbeRequested"/> so the host opens the mode-slice flyout.
    /// </summary>
    private void ProbeAt(double x, double y)
    {
        var component = ComponentAt(x, y);
        if (component != null)
        {
            var attachedWidth = _canvas.Connections
                .Where(c => c.Connection.StartPin.ParentComponent == component.Component
                         || c.Connection.EndPin.ParentComponent == component.Component)
                .Select(c => (double?)c.Connection.WidthMicrometers)
                .FirstOrDefault();
            var target = CAP_Core.Solvers.ModeProbe.ProbeTarget.ForComponent(component.Name, attachedWidth);
            ProbeRequested?.Invoke(target, x, y);
            return;
        }

        var connection = FindConnectionAt(x, y);
        if (connection != null)
        {
            var target = CAP_Core.Solvers.ModeProbe.ProbeTarget.ForConnection(
                connection.Connection.WidthMicrometers, connection.PathLength);
            ProbeRequested?.Invoke(target, x, y);
            return;
        }

        UpdateStatus?.Invoke("Probe mode: Click a waveguide or coupler to inspect its mode slice");
    }

    private WaveguideConnectionViewModel? FindConnectionAt(double x, double y)
    {
        // Hover and click share ONE hit test (arc-accurate) — a second,
        // chord-approximated copy of this logic produced the field report
        // "hover lights it up, but the click misses".
        return DesignCanvasHitTesting.HitTestConnection(new Point(x, y), _canvas);
    }

    /// <summary>
    /// Finds the canvas-level frozen path nearest the point (issue #856). Delegates to
    /// the shared canvas hit test so hover, click and delete all agree on what is hit.
    /// </summary>
    public CanvasFrozenPathViewModel? FindCanvasFrozenPathAt(double x, double y)
        => DesignCanvasHitTesting.HitTestCanvasFrozenPath(new Point(x, y), _canvas);

    [RelayCommand]
    private void SetSelectMode()
    {
        CurrentMode = InteractionMode.Select;
        SelectedTemplate = null;
        SelectedGroupTemplate = null;
        _connectionGesture.Reset();
    }

    [RelayCommand]
    private void SetConnectMode()
    {
        CurrentMode = InteractionMode.Connect;
        SelectedTemplate = null;
        SelectedGroupTemplate = null;
        _connectionGesture.Reset();
    }

    [RelayCommand]
    private void SetProbeMode()
    {
        CurrentMode = InteractionMode.Probe;
        SelectedTemplate = null;
        SelectedGroupTemplate = null;
        _connectionGesture.Reset();
    }

    /// <summary>
    /// Activates the analysis-output picker (#754): candidate couplers light up on the
    /// canvas and the next coupler click designates the analysis output.
    /// </summary>
    [RelayCommand]
    private void SetPickAnalysisOutputMode()
    {
        CurrentMode = InteractionMode.PickAnalysisOutput;
        SelectedTemplate = null;
        SelectedGroupTemplate = null;
        _connectionGesture.Reset();
    }

    /// <summary>
    /// Activates the Cut tool: pin guide lines and crossing-insertion
    /// candidates are shown; clicking a candidate inserts a crossing component.
    /// </summary>
    [RelayCommand]
    private void SetCutMode()
    {
        CurrentMode = InteractionMode.Cut;
        SelectedTemplate = null;
        SelectedGroupTemplate = null;
        _connectionGesture.Reset();
    }

    [RelayCommand]
    private void SetDeleteMode()
    {
        CurrentMode = InteractionMode.Delete;
        SelectedTemplate = null;
        SelectedGroupTemplate = null;
        _connectionGesture.Reset();
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        var selection = _canvas.Selection;

        // The selection set is authoritative (box selection populates only the set);
        // fall back to the primary SelectedComponent when the set is empty.
        var targets = selection.SelectedComponents.ToList();
        if (targets.Count == 0 && SelectedComponent is { IsSelected: true })
            targets.Add(SelectedComponent);

        var frozenPaths = selection.SelectedFrozenPaths.Where(_canvas.CanvasFrozenPaths.Contains).ToList();
        var connections = selection.SelectedConnections.ToList();
        if (SelectedWaveguideConnection is { IsSelected: true } single && !connections.Contains(single))
            connections.Add(single);

        var deletable = targets.Where(c => !c.Component.IsLocked && !_dieRules.IsUndeletableDie(c)).ToList();
        // Component deletion already owns its incident wires and their undo snapshots.
        var scopes = deletable.Select(c => new ComponentWireScope(c.Component)).ToList();
        connections = connections.Where(c => !c.Connection.IsLocked && _canvas.Connections.Contains(c) &&
            !scopes.Any(scope => scope.Touches(c.Connection))).ToList();
        if (deletable.Count == 0 && connections.Count == 0 && frozenPaths.Count == 0)
        {
            if (_dieRules.DeleteRefusal(targets) is { } refusal)
                UpdateStatus?.Invoke(refusal);
            else if (targets.Count > 0)
                UpdateStatus?.Invoke("Selection is locked — unlock elements to delete them");
            return;
        }

        var steps = new List<IUndoableCommand>();
        if (deletable.Count > 0)
            steps.Add(_dieRules.OnDieSurface(deletable, () => deletable.Count == 1
                ? new DeleteComponentCommand(_canvas, deletable[0])
                : new GroupDeleteCommand(_canvas, deletable), "Delete"));
        steps.AddRange(connections.Select(c => new DeleteConnectionCommand(_canvas, c)));
        steps.AddRange(frozenPaths.Select(p => new DeleteCanvasFrozenPathCommand(_canvas, p)));
        _commandManager.ExecuteCommand(steps.Count == 1 ? steps[0] : new CompositeCommand("Delete selection", steps));

        selection.ClearSelection();
        SelectedComponent = null;
        SelectedWaveguideConnection = null;
        SelectedCanvasFrozenPath = null;
        UpdateStatus?.Invoke(string.Format(
            Services.Localization.LocalizationService.Instance.Translate("Status.SelectedElementsDeleted"),
            deletable.Count + connections.Count + frozenPaths.Count));
    }



    [RelayCommand]
    private void CopySelected()
    {
        var selection = _canvas.Selection;
        if (!selection.HasSelection) return;

        var copied = selection.SelectedComponents.Where(c => !_canvas.ChipDie.IsProxy(c.Component)).ToList();
        if (copied.Count == 0) return;
        _canvas.Clipboard.Copy(copied, _canvas.Connections);

        UpdateStatus?.Invoke($"Copied {selection.SelectedComponents.Count} component(s)");
    }

    /// <summary>
    /// Pastes components from clipboard at the specified position.
    /// </summary>
    public void PasteSelected(double? targetX = null, double? targetY = null)
    {
        if (!_canvas.Clipboard.HasContent) return;

        // PeekEntryPdkSources expands groups to their resolved children (the clipboard's
        // PdkSourceResolver is wired by MainViewModel), so a copied group cannot
        // smuggle foreign-process components past the paste guard (issue #653).
        // Per entry (issue #935): a copied group whose children uniformly belong to one
        // other catalog process pastes as its own chiplet; loose foreign components
        // stay blocked by the canvas lock.
        var blockedCount = _canvas.Clipboard.PeekEntryPdkSources()
            .Count(entry => !PlacementContext.IsPasteEntryAllowed(entry.IsGroup, entry.Sources));
        if (blockedCount > 0)
        {
            // A blocked component implies a non-null, non-Playground active process.
            UpdateStatus?.Invoke(
                $"Clipboard has {blockedCount} component(s) from another process; " +
                $"cannot paste into the '{PlacementContext.ActiveProcess!.DisplayName}' design.");
            return;
        }

        var paste = new PasteComponentsCommand(_canvas, _canvas.Clipboard, targetX, targetY);
        _commandManager.ExecuteCommand(_canvas.Surface.IsActive
            ? new Commands.Dies.DieSurfaceCommand(_canvas, () => paste, Array.Empty<ComponentViewModel>(), _dieRules.DieUnder, "Paste")
            : paste);
        var cmd = paste;

        if (cmd.Result != null)
        {
            _canvas.Selection.ClearSelection();
            foreach (var comp in cmd.Result.Components)
            {
                comp.IsSelected = true;
                _canvas.Selection.SelectedComponents.Add(comp);
            }

            _ = _canvas.RecalculateRoutesAsync();
            UpdateStatus?.Invoke($"Pasted {cmd.Result.Components.Count} component(s)");
        }
    }

    [RelayCommand]
    private void PasteSelectedCommand()
    {
        PasteSelected();
    }

    [RelayCommand]
    private void RotateSelected()
    {
        if (SelectedComponent != null && !_canvas.ChipDie.IsProxy(SelectedComponent.Component))
        {
            var cmd = new RotateComponentCommand(_canvas, SelectedComponent);
            _commandManager.ExecuteCommand(cmd);
            UpdateStatus?.Invoke(cmd.WasApplied
                ? $"Rotated: {SelectedComponent.Name}"
                : $"Cannot rotate: {SelectedComponent.Name} would overlap another component");
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateGroup))]
    private void CreateGroup() => _grouping.Create(SelectedComponent);

    private bool CanCreateGroup()
    {
        return _canvas.Selection.SelectedComponents.Count >= 2 && !SelectionHasDie;
    }

    [RelayCommand(CanExecute = nameof(CanUngroup))]
    private void Ungroup() => _grouping.Ungroup();

    private bool CanUngroup()
    {
        return _canvas.Selection.SelectedComponents.Count == 1 &&
               _canvas.Selection.SelectedComponents.First().Component is CAP_Core.Components.Core.ComponentGroup { IsDie: false };
    }

    [RelayCommand(CanExecute = nameof(CanRenameGroup))]
    private Task RenameGroup() => _groupLibraryActions.RenameAsync(SelectedGroup());

    private ComponentGroup? SelectedGroup() => _canvas.Selection.SelectedComponents
        .Select(component => component.Component).OfType<ComponentGroup>().FirstOrDefault();

    private bool CanRenameGroup()
    {
        return _canvas.Selection.SelectedComponents.Count == 1 &&
               _canvas.Selection.SelectedComponents.First().Component is CAP_Core.Components.Core.ComponentGroup { IsDie: false } &&
               _groupLibraryActions.IsAvailable;
    }

    [RelayCommand(CanExecute = nameof(CanSaveGroupAs))]
    private Task SaveGroupAs() => _groupLibraryActions.SaveAsAsync(SelectedGroup());

    private bool CanSaveGroupAs()
    {
        return _canvas.Selection.SelectedComponents.Count == 1 &&
               _canvas.Selection.SelectedComponents.First().Component is CAP_Core.Components.Core.ComponentGroup &&
               _groupLibraryActions.IsAvailable;
    }

    /// <summary>
    /// Opens the unified "Edit Component" editor for the currently selected canvas component's
    /// PDK template, or the per-instance Component Settings dialog when no editable template
    /// resolves (e.g. ComponentGroups). Only enabled when a component is selected.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenSelectedComponentSettings))]
    private void OpenSelectedComponentSettings()
    {
        var selected = SelectedComponent;
        if (selected != null && !IsDie(selected))
            OpenComponentSettings?.Invoke(selected);
    }

    private bool CanOpenSelectedComponentSettings()
        => SelectedComponent != null && !IsDie(SelectedComponent);
}
