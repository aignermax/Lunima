using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP_Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing.CrossingInsertion;
using CAP_Core.Routing.CrossingInsertion.ManualCrossing;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Inserts a user-placed crossing component at a Cut-tool candidate point:
/// centers the PDK crossing on the intersection, removes the original connection and
/// docks its two halves onto the crossing's through ports. Unlike adaptive crossings,
/// the component is deliberate user intent: <c>IsInsertedCrossing</c>
/// stays false, so the adaptive pass never dissolves or moves it. The halves keep the cut
/// wire's exact geometry (see <see cref="ManualCrossingRouteSplitter"/>), so cutting any wire —
/// including a piece of a routing-placed crossing chain — never re-routes it or takes the
/// chain out. Undo removes the crossing and restores the original connection object — its
/// fine-tuning (bend-radius overrides, segment shifts) survives untouched.
/// </summary>
public class InsertManualCrossingCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ManualCrossingCandidate _candidate;
    private readonly CrossingComponentInstance _crossingInstance;
    private readonly WaveguideConnection _original;
    private readonly ErrorConsoleService? _errorConsole;

    private ComponentViewModel? _componentViewModel;
    private List<WaveguideConnection>? _subConnections;
    private List<CrossingRecord> _releasedRecords = new();
    private bool _failed;

    /// <summary>Creates the command for one candidate and a fresh crossing instance.</summary>
    /// <param name="errorConsole">
    /// Optional sink for the post-mutation routing pass's exceptions, which run fire-and-forget
    /// (see <see cref="Execute"/>) and must never crash unobserved on a background thread.
    /// </param>
    public InsertManualCrossingCommand(
        DesignCanvasViewModel canvas,
        ManualCrossingCandidate candidate,
        CrossingComponentInstance crossingInstance,
        ErrorConsoleService? errorConsole = null)
    {
        _canvas = canvas;
        _candidate = candidate;
        _crossingInstance = crossingInstance;
        _original = candidate.Connection;
        _errorConsole = errorConsole;
    }

    /// <summary>
    /// False when the last <see cref="Execute"/> was a safe no-op: the connection is no longer
    /// in the design, or the crossing lacks a port the split needs.
    /// </summary>
    public bool Succeeded => !_failed && _subConnections != null;

    /// <summary>The crossing component this command inserts.</summary>
    public Component Crossing => _crossingInstance.Component;

    /// <summary>The routing pass the last Execute/Undo started; completes when the design is routed.</summary>
    public Task RerouteTask { get; private set; } = Task.CompletedTask;

    /// <inheritdoc/>
    public string Description => $"Insert crossing into {_original.StartPin.Name}–{_original.EndPin.Name}";

    /// <inheritdoc/>
    public void Execute()
    {
        if (_failed) return;
        if (_subConnections == null && !TryPrepare()) return;

        var crossing = _crossingInstance.Component;
        CenterCrossingOnIntersection(crossing);
        _componentViewModel = _canvas.AddComponent(
            crossing, _crossingInstance.TemplateName, _crossingInstance.TemplatePdkSource);

        ReleaseRecordsOfDockedCrossings();
        Swap(new[] { _original }, _subConnections!);

        _canvas.InvalidateSimulation();
        FireAndForgetReroute();
    }

    /// <inheritdoc/>
    public void Undo()
    {
        if (_failed || _subConnections == null || _componentViewModel == null) return;

        Swap(_subConnections, new[] { _original });
        _canvas.Router.RemoveComponentObstacle(_crossingInstance.Component);
        CrossingCanvasSync.RemoveCrossing(_canvas, _crossingInstance.Component);

        foreach (var record in _releasedRecords)
            _canvas.ConnectionManager.CrossingService?.RestoreRecord(record);
        _releasedRecords = new List<CrossingRecord>();
        _canvas.InvalidateSimulation();
        FireAndForgetReroute();
    }

    /// <summary>
    /// First-run setup: resolves the crossing's four cardinal ports and builds the
    /// two half-connections. Marks the command failed (a safe no-op) when the component
    /// lacks a required port, or when the original connection is no longer registered with
    /// the canvas — a stale candidate re-clicked before the next frame refreshed the
    /// candidate list must not stack a second crossing onto an already-split connection.
    /// Nothing was mutated yet at either failure point.
    /// </summary>
    private bool TryPrepare()
    {
        if (!_canvas.ConnectionManager.Connections.Contains(_original))
        {
            _failed = true;
            return false;
        }

        var crossing = _crossingInstance.Component;
        CenterCrossingOnIntersection(crossing);

        var (entry, exit) = CrossingPlacement.ResolveThroughPorts(
            crossing, _candidate.SegmentIsHorizontal, _candidate.SegmentDirection);
        if (entry?.LogicalPin == null || exit?.LogicalPin == null)
        {
            _failed = true;
            return false;
        }

        _subConnections = new List<WaveguideConnection>
        {
            CrossingPlacement.CreateSubConnection(_original, _original.StartPin, entry),
            CrossingPlacement.CreateSubConnection(_original, exit, _original.EndPin),
        };
        KeepCutGeometry(entry, exit);
        return true;
    }

    /// <summary>
    /// Hands the halves the cut wire's own route. Where the route cannot be cut cleanly (no
    /// straight under the crossing, a blocked placeholder) the halves stay route-less and the
    /// next pass routes them.
    /// </summary>
    private void KeepCutGeometry(PhysicalPin entry, PhysicalPin exit)
    {
        if (!ManualCrossingRouteSplitter.TrySplit(_original, _candidate.IntersectionPoint, entry, exit, out var before, out var after))
            return;
        _subConnections![0].RestoreCachedPath(before);
        _subConnections[1].RestoreCachedPath(after);
        foreach (var sub in _subConnections)
            sub.IsRouteFrozen = _original.IsRouteFrozen;
    }

    /// <summary>
    /// A wire cut next to a routing-placed crossing that still has a record (an isolated
    /// detour crossing) would have its record restore the pre-split originals over the cut:
    /// the record is handed to chain bookkeeping first and put back on undo.
    /// </summary>
    private void ReleaseRecordsOfDockedCrossings()
    {
        _releasedRecords = new List<CrossingRecord>();
        var service = _canvas.ConnectionManager.CrossingService;
        if (service == null) return;
        foreach (var crossing in new[] { _original.StartPin.ParentComponent, _original.EndPin.ParentComponent })
        {
            if (crossing != null && service.ReleaseRecord(crossing) is { } record)
                _releasedRecords.Add(record);
        }
    }

    private void CenterCrossingOnIntersection(Component crossing)
    {
        crossing.PhysicalX = _candidate.IntersectionPoint.X - crossing.WidthMicrometers / 2.0;
        crossing.PhysicalY = _candidate.IntersectionPoint.Y - crossing.HeightMicrometers / 2.0;
    }

    /// <summary>
    /// Swaps the wires in the manager and their view-models. A swap whose added wires keep
    /// their geometry leaves the blocked wires' verdicts alone, so the follow-up pass does not
    /// re-search the whole design for a cut that changed no geometry.
    /// </summary>
    private void Swap(IReadOnlyCollection<WaveguideConnection> removed, IReadOnlyCollection<WaveguideConnection> added)
    {
        _canvas.ConnectionManager.SwapConnections(removed, added);
        foreach (var vm in _canvas.Connections.Where(c => removed.Contains(c.Connection)).ToList())
            _canvas.Connections.Remove(vm);
        foreach (var connection in added.Where(c => !_canvas.Connections.Any(vm => vm.Connection == c)))
            _canvas.Connections.Add(new WaveguideConnectionViewModel(connection));
    }

    /// <summary>
    /// Starts the post-mutation reroute without blocking Execute/Undo on it — awaiting here
    /// would risk a UI-thread deadlock against the pass's own dispatcher-posted continuations
    /// (see <c>RoutingOrchestrator</c>). Observes the task instead of discarding it outright,
    /// so a failure surfaces through the error console rather than as an unobserved exception.
    /// </summary>
    private void FireAndForgetReroute()
    {
        RerouteTask = ObserveRerouteAsync();
    }

    private async Task ObserveRerouteAsync()
    {
        try
        {
            await _canvas.RecalculateRoutesAsync();
        }
        catch (Exception ex)
        {
            _errorConsole?.LogError("Cut tool: routing after crossing insert/undo failed", ex);
        }
    }
}
