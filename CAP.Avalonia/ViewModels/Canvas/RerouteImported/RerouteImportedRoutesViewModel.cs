using System.Globalization;
using System.Linq;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_Core.Routing.RerouteImported;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Canvas.RerouteImported;

/// <summary>
/// ViewModel for re-routing imported (frozen) waveguide routes on demand.
/// Counts the design's frozen imported routes, offers "re-route all" / "re-route
/// selected" as ONE undoable command, and reports the before/after length and bend
/// delta so the user sees what the router changed. Hand-edited frozen routes are
/// never re-routed; their count is surfaced instead. Frozen routes living inside
/// component groups (the standard GDS import groups everything it placed) are
/// counted and re-routed in place without dissolving the group.
/// </summary>
public partial class RerouteImportedRoutesViewModel : ObservableObject
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly CommandManager _commandManager;
    private int _editRevision;

    /// <summary>Number of frozen imported routes the router may replace.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFrozenImportedRoutes))]
    [NotifyPropertyChangedFor(nameof(FrozenCountText))]
    [NotifyPropertyChangedFor(nameof(IsPanelVisible))]
    [NotifyCanExecuteChangedFor(nameof(RerouteAllCommand))]
    private int _frozenImportedCount;

    /// <summary>Frozen routes kept unchanged because they carry manual edits.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHandEditedFrozenRoutes))]
    [NotifyPropertyChangedFor(nameof(HandEditedKeptText))]
    [NotifyPropertyChangedFor(nameof(IsPanelVisible))]
    private int _handEditedFrozenCount;

    /// <summary>Re-routable frozen routes living inside component groups.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGroupedFrozenRoutes))]
    [NotifyPropertyChangedFor(nameof(GroupedFrozenText))]
    [NotifyPropertyChangedFor(nameof(IsPanelVisible))]
    private int _groupedFrozenCount;

    /// <summary>Localized "N frozen imported route(s)" line for the panel.</summary>
    public string FrozenCountText => string.Format(CultureInfo.CurrentCulture,
        LocalizationService.Instance.Translate("Routing.Reroute.FrozenCount"), FrozenImportedCount);

    /// <summary>Localized "N hand-edited route(s) kept unchanged" line for the panel.</summary>
    public string HandEditedKeptText => string.Format(CultureInfo.CurrentCulture,
        LocalizationService.Instance.Translate("Routing.Reroute.HandEditedKept"), HandEditedFrozenCount);

    /// <summary>Localized "N frozen route(s) inside groups" line for the panel.</summary>
    public string GroupedFrozenText => string.Format(CultureInfo.CurrentCulture,
        LocalizationService.Instance.Translate("Routing.Reroute.InGroups"), GroupedFrozenCount);

    /// <summary>Before/after summary of the last re-route pass, empty until one ran.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPanelVisible))]
    private string _resultText = "";

    /// <summary>True while a re-route pass is running (buttons disable).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RerouteAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(RerouteSelectedCommand))]
    private bool _isRerouting;

    /// <summary>True when the design contains at least one re-routable imported route.</summary>
    public bool HasFrozenImportedRoutes => FrozenImportedCount > 0;

    /// <summary>True when hand-edited frozen routes exist (shown as "kept unchanged").</summary>
    public bool HasHandEditedFrozenRoutes => HandEditedFrozenCount > 0;

    /// <summary>True when re-routable frozen routes live inside component groups.</summary>
    public bool HasGroupedFrozenRoutes => GroupedFrozenCount > 0;

    /// <summary>
    /// Panel visibility: shown while frozen imported routes exist (on the canvas or
    /// inside groups) AND kept visible after a full re-route so the before/after
    /// delta and the "kept unchanged" note stay readable.
    /// </summary>
    public bool IsPanelVisible =>
        HasFrozenImportedRoutes || HasHandEditedFrozenRoutes || HasGroupedFrozenRoutes
        || ResultText.Length > 0;

    private WaveguideConnectionViewModel? _selectedConnection;

    /// <summary>Shows the source-preservation explanation and explicit editing action.</summary>
    public bool HasSelectedDrawnRoute => SelectedConnection?.Connection.AsDrawnGeometry is not null;

    /// <summary>The currently selected connection, fed by <c>MainViewModel</c>.</summary>
    public WaveguideConnectionViewModel? SelectedConnection
    {
        get => _selectedConnection;
        set
        {
            if (SetProperty(ref _selectedConnection, value))
            {
                OnPropertyChanged(nameof(HasSelectedDrawnRoute));
                RerouteSelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Initializes the ViewModel and tracks connection/undo state changes.</summary>
    public RerouteImportedRoutesViewModel(DesignCanvasViewModel canvas, CommandManager commandManager)
    {
        _canvas = canvas;
        _commandManager = commandManager;
        _canvas.Connections.CollectionChanged += (_, _) => Refresh();
        // Grouping (the standard GDS import ends with one) moves frozen routes off the
        // canvas into the group — the Components collection is where that shows up.
        _canvas.Components.CollectionChanged += (_, _) => Refresh();
        // Routing passes flip IsRouteFrozen outside commands (e.g. an endpoint moved
        // unfreezes the route); StateChanged fires after every pass.
        _canvas.Routing.StateChanged += Refresh;
        _commandManager.StateChanged += (_, _) => { _editRevision++; Refresh(); };
        _commandManager.HistoryCleared += () => _editRevision++;
    }

    /// <summary>
    /// Recounts eligible and hand-edited frozen routes from the canvas plus the
    /// re-routable frozen paths inside component groups. An emptied canvas (new or
    /// switched design) also discards the last re-route delta.
    /// </summary>
    public void Refresh()
    {
        if (IsRerouting) return;
        var eligible = 0;
        var handEdited = 0;
        foreach (var connection in _canvas.Connections)
        {
            if (ImportedRouteRerouteEligibility.IsEligible(connection.Connection))
                eligible++;
            else if (ImportedRouteRerouteEligibility.IsKeptHandEdited(connection.Connection))
                handEdited++;
        }
        FrozenImportedCount = eligible;
        HandEditedFrozenCount = handEdited;
        GroupedFrozenCount = _canvas.Components
            .Select(c => c.Component)
            .OfType<ComponentGroup>()
            .Sum(ImportedRouteRerouteEligibility.CountEligibleGroupInternal);
        if (_canvas.Components.Count == 0 && _canvas.Connections.Count == 0)
            ResultText = "";
        RerouteSelectedCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasSelectedDrawnRoute));
    }

    /// <summary>Re-routes every eligible frozen imported route in the design.</summary>
    [RelayCommand(CanExecute = nameof(CanRerouteAll))]
    private async Task RerouteAll()
    {
        var canvasTargets = _canvas.Connections
            .Where(c => ImportedRouteRerouteEligibility.IsEligible(c.Connection))
            .ToList();
        var groupTargets = CollectGroupTargets(_canvas).ToList();

        if (canvasTargets.Count == 0 && groupTargets.Count == 0)
            return;

        await RerouteAllAsync(canvasTargets, groupTargets);
    }

    private bool CanRerouteAll() =>
        !IsRerouting && (FrozenImportedCount > 0 || GroupedFrozenCount > 0);

    /// <summary>Re-routes only the selected connection.</summary>
    [RelayCommand(CanExecute = nameof(CanRerouteSelected))]
    private async Task RerouteSelected()
    {
        if (SelectedConnection is { } selected)
            await RerouteAsync(new List<WaveguideConnectionViewModel> { selected });
    }

    private bool CanRerouteSelected() =>
        !IsRerouting
        && SelectedConnection is { } selected
        && ImportedRouteRerouteEligibility.IsEligible(selected.Connection);

    private async Task RerouteAllAsync(
        IReadOnlyList<WaveguideConnectionViewModel> canvasTargets,
        IReadOnlyList<(ComponentGroup Group, List<FrozenWaveguidePath> Paths)> groupTargets)
    {
        IsRerouting = true;
        var expectedScope = _canvas.CurrentEditGroup;
        var originalScopeChildren = expectedScope?.ChildComponents.ToArray();
        RerouteImportedRoutesStateCommand? stateCommand = null;
        bool stopped = false;
        void OnStop() => stopped = true;
        _canvas.Routing.StopRequested += OnStop;
        try
        {
            var sourcePaths = canvasTargets.Select(t => t.Connection.RoutedPath).ToArray();
            var generation = _canvas.Routing.MutationGeneration;
            var revision = _editRevision;
            var scopePaths = expectedScope != null && canvasTargets.Count > 0 ? CaptureScopePaths(expectedScope) : null;
            var rootTopology = expectedScope == null && canvasTargets.Count > 0 && _canvas.ConnectionManager.CrossingService != null
                ? new RerouteImportedRootTopology(_canvas) : null;
            var (connectionStates, groupStates, originalScopePaths) = await PrepareSnapshotsAsync(canvasTargets, groupTargets, scopePaths, rootTopology);
            if (stopped || generation != _canvas.Routing.MutationGeneration || revision != _editRevision
                || !ReferenceEquals(_canvas.CurrentEditGroup, expectedScope)
                || canvasTargets.Where((t, i) => !_canvas.Connections.Contains(t)
                    || !ReferenceEquals(t.Connection.RoutedPath, sourcePaths[i])
                    || !ImportedRouteRerouteEligibility.IsEligible(t.Connection)).Any()
                || groupTargets.Any(g => !ContainsCurrentGroup(g.Group))) return;

            var allConnectionTargets = canvasTargets.Select(t => t.Connection).ToList();
            var allGroupPaths = groupTargets.SelectMany(g => g.Paths).ToList();
            var before = RouteMetricsSnapshot.Capture(allConnectionTargets, allGroupPaths);

            stateCommand = new RerouteImportedRoutesStateCommand(_canvas, connectionStates, groupStates);
            stateCommand.RootTopology = rootTopology;
            stateCommand.RoutingGroup = expectedScope;
            _commandManager.BeginExecution(stateCommand);

            // Canvas-level targets are mutated directly; the state command handles undo.
            int kept = 0;
            if (canvasTargets.Count > 0)
            {
                var drawn = canvasTargets.Select((target, index) => new DrawnRouteSnapshot(
                    target, connectionStates[index].OldPath, connectionStates[index].OldAsDrawn)).ToList();
                UnfreezeTargets(canvasTargets);
                await _canvas.RecalculateRoutesAsync();
                kept = KeepDrawnRouteWhereBlocked(drawn);
                stateCommand.RootTopology?.CaptureAfter();
            }

            // Group-internal targets are routed inside a temporary group-edit sub-canvas.
            var completedGroups = new HashSet<ComponentGroup>();
            foreach (var (group, paths) in groupTargets)
            {
                if (stopped || !ReferenceEquals(_canvas.CurrentEditGroup, expectedScope)) break;
                var result = await RerouteGroupInternalAsync(group, paths, expectedScope, () => stopped, stateCommand);
                if (result.Completed)
                    completedGroups.Add(group);
                kept += result.Kept;
            }
            groupStates.RemoveAll(state => !completedGroups.Contains(state.Group));
            if (originalScopePaths != null)
            {
                // Leaving an edited group serializes its wires and discards the live objects.
                // Preserve that completed work through the existing group-path undo snapshot.
                groupStates.Add(new RerouteImportedRoutesStateCommand.GroupState
                {
                    Group = expectedScope!, OldPaths = originalScopePaths, OldChildren = originalScopeChildren,
                    TargetPins = canvasTargets.Select(target => ((PhysicalPin?)target.Connection.StartPin, (PhysicalPin?)target.Connection.EndPin)).ToArray()
                });
            }

            // A target may have been deleted, or the document replaced, while routing awaited.
            // Keep successful work on surviving objects, never old-document undo snapshots.
            var liveConnections = (expectedScope == null ? _canvas.Groups.RootConnections : _canvas.Connections)
                .Select(vm => vm.Connection).ToHashSet(ReferenceEqualityComparer.Instance);
            if (expectedScope != null) connectionStates.Clear();
            connectionStates.RemoveAll(state => (!liveConnections.Contains(state.Connection)
                && stateCommand.RootTopology?.Replaced(state.Connection) != true)
                || state.Connection.RoutedPath is null);
            groupStates.RemoveAll(state => !_canvas.Groups.RootComponents
                .Select(vm => vm.Component).OfType<ComponentGroup>()
                .Any(group => ContainsGroup(group, state.Group)));
            if (connectionStates.Count == 0 && groupStates.Count == 0
                && stateCommand.RootTopology?.HasChanges != true) return;

            CaptureFinalSnapshots(connectionStates, groupStates);
            if (originalScopePaths != null && ReferenceEquals(_canvas.CurrentEditGroup, expectedScope))
            {
                var scopeState = groupStates.First(s => ReferenceEquals(s.Group, expectedScope));
                var paths = CaptureScopePaths(expectedScope!);
                scopeState.NewChildren = _canvas.Components.Select(c => c.Component).ToArray();
                scopeState.NewPathCount = paths.Count;
                scopeState.CreateNewPaths = () => paths.Select(build => build()).ToList();
            }

            stateCommand.SealGroupTopology();
            _commandManager.ExecuteCommand(stateCommand);

            var afterConnections = connectionStates.Select(s => s.Connection);
            var afterGroupPaths = groupStates.SelectMany(s => s.Group.InternalPaths);
            var after = RouteMetricsSnapshot.Capture(afterConnections, afterGroupPaths);
            ResultText = WithKeptNote(FormatDelta(
                canvasTargets.Count + groupTargets.Sum(g => g.Paths.Count), before, after), kept);
        }
        finally
        {
            if (stateCommand != null) _commandManager.CancelExecution(stateCommand);
            _canvas.Routing.StopRequested -= OnStop;
            IsRerouting = false;
            Refresh();
        }
    }

    private async Task<(bool Completed, int Kept)> RerouteGroupInternalAsync(
        ComponentGroup group, IReadOnlyList<FrozenWaveguidePath> paths, ComponentGroup? expectedScope,
        Func<bool> stopped, RerouteImportedRoutesStateCommand stateCommand)
    {
        // Drain the previous canvas worker before replacing its live connection list.
        // The gate also rejects queued crossing callbacks from the old scope.
        bool entered = await _canvas.Routing.ExecuteHistoryChangeAsync(() =>
        {
            _canvas.EnterGroupEditMode(group);
            return true;
        }, () => !stopped() && ReferenceEquals(_canvas.CurrentEditGroup, expectedScope) && ContainsCurrentGroup(group));
        if (!entered) return (false, 0);
        int kept = 0;
        try
        {
            if (stopped()) return (false, 0);
            stateCommand.RoutingGroup = group;
            var sourcePaths = paths.GroupBy(p => (p.StartPin, p.EndPin))
                .ToDictionary(g => g.Key, g => g.First());
            var targets = _canvas.Connections
                .Where(vm => sourcePaths.ContainsKey((vm.Connection.StartPin, vm.Connection.EndPin)))
                .ToList();
            var drawn = targets.Select(target =>
            {
                var source = sourcePaths[(target.Connection.StartPin, target.Connection.EndPin)];
                return new DrawnRouteSnapshot(target, source.Path, source.AsDrawnGeometry);
            }).ToList();

            if (targets.Count > 0)
                UnfreezeTargets(targets);

            await _canvas.RecalculateRoutesAsync();
            kept = KeepDrawnRouteWhereBlocked(drawn);
        }
        finally
        {
            if (ReferenceEquals(_canvas.CurrentEditGroup, group))
                _canvas.ExitGroupEditMode();
        }
        return (true, kept);
    }

    /// <summary>
    /// Re-routes <paramref name="targets"/> through the same snapshot-based flow as
    /// "re-route all": the undo step records the FINAL state (including routes kept drawn
    /// because no free path existed), so redo reproduces exactly what the user saw.
    /// </summary>
    private Task RerouteAsync(IReadOnlyList<WaveguideConnectionViewModel> targets) =>
        targets.Count == 0
            ? Task.CompletedTask
            : RerouteAllAsync(targets, Array.Empty<(ComponentGroup, List<FrozenWaveguidePath>)>());

    private static string FormatDelta(int count, RouteMetricsSnapshot before, RouteMetricsSnapshot after) =>
        string.Format(CultureInfo.CurrentCulture,
            LocalizationService.Instance.Translate("Routing.Reroute.Result"),
            count,
            before.LengthMicrometers, after.LengthMicrometers,
            before.EquivalentBends, after.EquivalentBends);

    private bool ContainsCurrentGroup(ComponentGroup target) =>
        _canvas.Components.Select(vm => vm.Component).OfType<ComponentGroup>()
            .Any(group => ContainsGroup(group, target));

    private static bool ContainsGroup(ComponentGroup group, ComponentGroup target) =>
        ReferenceEquals(group, target) || group.ChildComponents.OfType<ComponentGroup>()
            .Any(child => ContainsGroup(child, target));

    private static IEnumerable<(ComponentGroup Group, List<FrozenWaveguidePath> Paths)> CollectGroupTargets(
        DesignCanvasViewModel canvas)
    {
        return canvas.Components
            .Select(c => c.Component)
            .OfType<ComponentGroup>()
            .SelectMany(CollectGroupTargetsRecursive);
    }

    private static IEnumerable<(ComponentGroup Group, List<FrozenWaveguidePath> Paths)> CollectGroupTargetsRecursive(
        ComponentGroup group)
    {
        var direct = group.InternalPaths
            .Where(ImportedRouteRerouteEligibility.IsEligibleGroupInternal)
            .ToList();
        if (direct.Count > 0)
            yield return (group, direct);

        foreach (var childGroup in group.ChildComponents.OfType<ComponentGroup>())
        {
            foreach (var nested in CollectGroupTargetsRecursive(childGroup))
                yield return nested;
        }
    }
}
