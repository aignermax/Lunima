using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;

/// <summary>
/// Wires the adaptive crossing-insertion feature (Issue #553) into the design
/// canvas: constructs the <see cref="CrossingInsertionService"/> with a factory
/// producing real PDK crossing components, assigns it to the canvas'
/// <see cref="WaveguideConnectionManager"/>, and keeps the canvas view-model
/// collections (components, pins, connections) in sync when crossings are
/// inserted or dissolved — so rendering, export and hierarchy see the crossing
/// like a normally placed component.
/// </summary>
// System.ComponentModel isn't `using`-imported here: its Component type collides with
// CAP_Core.Components.Core.Component, which this file uses throughout — so
// INotifyPropertyChanged/PropertyChangedEventHandler/-Args below are fully qualified.
public class CrossingInsertionCanvasBinder : System.ComponentModel.INotifyPropertyChanged
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly Func<CrossingComponentInstance?> _crossingFactory;
    private readonly Action<Action> _dispatch;

    private string? _templateName;
    private string? _templatePdkSource;

    /// <summary>The crossing-insertion service wired to the canvas' connection manager.</summary>
    public CrossingInsertionService Service { get; }

    /// <summary>
    /// Records what the re-routes after each edit change on the router-placed crossings, so
    /// undoing the edit restores them; attach it to the undo history with
    /// <see cref="CrossingUndoJournal.Attach"/>.
    /// </summary>
    public CrossingUndoJournal UndoJournal { get; }

    /// <summary>
    /// Creates the binder and attaches crossing insertion to the given canvas.
    /// </summary>
    /// <param name="canvas">The design canvas whose connection manager gets the crossing pass.</param>
    /// <param name="crossingFactory">
    /// Factory producing a fresh crossing component from the loaded PDK library.
    /// May return null while no crossing PDK component is available — the pass
    /// is then skipped and detours are kept.
    /// </param>
    /// <param name="uiDispatch">
    /// Marshals canvas collection updates to the UI thread. Defaults to the
    /// Avalonia UI dispatcher; tests can pass an inline dispatcher.
    /// </param>
    public CrossingInsertionCanvasBinder(
        DesignCanvasViewModel canvas,
        Func<CrossingComponentInstance?> crossingFactory,
        Action<Action>? uiDispatch = null)
    {
        _canvas = canvas;
        _crossingFactory = crossingFactory;
        _dispatch = uiDispatch ?? DispatchToUiThread;

        Service = new CrossingInsertionService(CreateCrossingComponent)
        {
            ComponentAdded = OnCrossingAdded,
            ComponentRemoved = OnCrossingRemoved,
        };
        // Crossings routing placed stay temporary even with insertion off: the service is
        // attached for maintenance right away; the composition root then restores the
        // user's insertion preference (enabled by default) without starting a routing pass.
        canvas.ConnectionManager.CrossingMaintenance = Service;
        UndoJournal = new CrossingUndoJournal(canvas, OnCrossingAdded, OnCrossingRemoved);
        Service.ChangeRecorded = UndoJournal.Record;
    }

    /// <summary>
    /// Whether adaptive crossing insertion runs after routing (Issue #553). The composition
    /// root restores the user's preference. Toggling on attaches the service; toggling
    /// off detaches it, so routing immediately reverts to classic avoid-only behavior
    /// (no extra post-routing pass). Crossings routing already placed stay until their
    /// wires go stale — the maintenance attachment then takes them out either way.
    /// Bound to the Routing settings page.
    /// </summary>
    public bool IsEnabled
    {
        get => _canvas.ConnectionManager.CrossingInsertion != null;
        set
        {
            if (value == IsEnabled) return;
            _canvas.ConnectionManager.CrossingInsertion = value ? Service : null;
            if (value)
                RebuildRecordsForExistingCrossings();
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }

    /// <summary>
    /// Raised when <see cref="IsEnabled"/> changes. The composition root subscribes to
    /// persist the user's choice, using the same INotifyPropertyChanged convention as the
    /// canvas' other Routing-settings flags (e.g.
    /// <see cref="DesignCanvasViewModel.UseDiagonalRouting"/>) — so the next Routing
    /// settings flag has one clear pattern to follow, not two.
    /// </summary>
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Rebuilds dissolution records for auto-inserted crossings that exist on the
    /// canvas without a record (#705) — e.g. loaded from file, or placed before the
    /// feature was toggled off and on again. Idempotent: recorded crossings are skipped.
    /// </summary>
    private void RebuildRecordsForExistingCrossings()
    {
        CrossingRecordRebuilder.Rebuild(
            Service,
            _canvas.ConnectionManager,
            _canvas.Components.Select(vm => vm.Component));
    }

    /// <summary>Creates a fresh crossing component and remembers its template metadata.</summary>
    private Component? CreateCrossingComponent()
    {
        var instance = _crossingFactory();
        if (instance == null) return null;

        _templateName = instance.TemplateName;
        _templatePdkSource = instance.TemplatePdkSource;
        return instance.Component;
    }

    /// <summary>
    /// Called by the service (on the routing thread) after a crossing was placed.
    /// Snapshots the connection list on the calling thread, then updates the
    /// canvas view-model collections on the UI thread.
    /// </summary>
    private void OnCrossingAdded(Component crossing)
    {
        var generation = _canvas.Routing.MutationGeneration;
        var connections = CrossingCanvasSync.SnapshotConnections(_canvas);
        _dispatch(() =>
        {
            if (generation != _canvas.Routing.MutationGeneration) return;
            // A chain put back after a dissolution keeps the template it was shown with.
            var (templateName, pdkSource) = _removedTemplates.Remove(crossing, out var known)
                ? known
                : (_templateName, _templatePdkSource);
            CrossingCanvasSync.AddCrossing(_canvas, crossing, templateName, pdkSource);
            CrossingCanvasSync.SyncConnections(_canvas, connections);
        });
    }

    /// <summary>Called by the service after a crossing was dissolved.</summary>
    private void OnCrossingRemoved(Component crossing)
    {
        var generation = _canvas.Routing.MutationGeneration;
        var connections = CrossingCanvasSync.SnapshotConnections(_canvas);
        _dispatch(() =>
        {
            if (generation != _canvas.Routing.MutationGeneration) return;
            if (CrossingCanvasSync.RemoveCrossing(_canvas, crossing) is { } vm)
            {
                if (_removedTemplates.Count >= MaxRememberedTemplates)
                    _removedTemplates.Clear();
                _removedTemplates[crossing] = (vm.TemplateName, vm.TemplatePdkSource);
            }
            CrossingCanvasSync.SyncConnections(_canvas, connections);
        });
    }

    /// <summary>
    /// Template metadata of crossings taken out during the current session, so a chain that
    /// is put back within the same routing pass looks exactly as before.
    /// </summary>
    private readonly Dictionary<Component, (string? Name, string? PdkSource)> _removedTemplates = new();

    /// <summary>Bound on <see cref="_removedTemplates"/>; only the most recent pass needs it.</summary>
    private const int MaxRememberedTemplates = 512;

    /// <summary>Runs the action on the Avalonia UI thread (inline when already on it).</summary>
    private static void DispatchToUiThread(Action action)
    {
        var dispatcher = global::Avalonia.Threading.Dispatcher.UIThread;
        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.Post(action);
    }
}
