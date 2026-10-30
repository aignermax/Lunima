using System.Collections.ObjectModel;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Services.Lifetime;
using CAP_Core.Components;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Documents;
using CAP_Core.Routing;
using CAP_Core.Routing.MeanderGeneration;
using CAP_Core.Routing.Snapshots;
using CAP_Contracts;

namespace CAP.Avalonia.ViewModels.Canvas.Services;

/// <summary>
/// Manages asynchronous waveguide route calculation with cancellation, throttling, and grid initialization.
/// </summary>
public partial class RoutingOrchestrator
{
    private readonly WaveguideRouter _router;
    private readonly WaveguideConnectionManager _connectionManager;
    private readonly ObservableCollection<ComponentViewModel> _components;
    private readonly ObservableCollection<WaveguideConnectionViewModel> _connections;
    private readonly DocumentSession _document;

    private CancellationTokenSource? _routingCts;
    private readonly SemaphoreSlim _routingSemaphore = new(1, 1);

    private int _routedCount;
    private int _totalCount;
    private int _passId;
    private int _activePassId;
    private DateTime _passStartUtc;

    /// <summary>
    /// Default canvas bounds for A* pathfinding grid (in micrometers).
    /// </summary>
    private const double DefaultGridMinX = -100;
    private const double DefaultGridMinY = -100;
    private const double DefaultGridMaxX = 5100;
    private const double DefaultGridMaxY = 5100;

    /// <summary>
    /// Minimum clearance between waveguides and components (in micrometers).
    /// </summary>
    private const double ComponentClearanceMicrometers = 5.0;

    /// <summary>
    /// Whether routing is currently in progress.
    /// </summary>
    public bool IsRouting { get; private set; }

    /// <summary>
    /// Status text for routing progress.
    /// </summary>
    public string RoutingStatusText { get; private set; } = "";

    /// <summary>
    /// Connections routed so far in the current (or last) pass. Updated on the routing
    /// thread as each connection finishes; the throttled status text reads it.
    /// </summary>
    public int RoutedConnectionCount => _routedCount;

    /// <summary>
    /// Total number of connections in the current (or last) pass.
    /// </summary>
    public int TotalConnectionCount => _totalCount;

    /// <summary>
    /// Callback invoked when the canvas needs to be repainted during progressive updates.
    /// </summary>
    public Action? RepaintRequested { get; set; }

    /// <summary>
    /// Callback returning the minimum waveguide bend radius (µm) allowed by the design's active
    /// fabrication process (wired by <c>MainViewModel</c> to <c>WaveguideBendRadiusResolver</c>,
    /// same source as the bend-handle clamp). Consulted at the start of every routing pass and
    /// pushed onto <see cref="WaveguideRouter.ProcessMinBendRadiusMicrometers"/>, so a process
    /// switch takes effect on the next reroute. When unwired, the router floor stays unchanged.
    /// </summary>
    public Func<double>? GetProcessMinBendRadiusMicrometers { get; set; }

    /// <summary>
    /// Callback returning the active process' metal routing spec (wired by <c>MainViewModel</c>
    /// to <c>MetalRoutingSpecFactory</c>, the same provider the exporters use). Consulted at the
    /// start of every routing pass: its bend radius becomes the router's metal floor and its
    /// trace width the obstacle padding for electrical connections (issue #854). When unwired,
    /// the metal defaults stay unchanged.
    /// </summary>
    public Func<CAP_Core.Routing.MetalRouting.MetalRoutingSpec>? GetMetalRoutingSpec { get; set; }

    /// <summary>
    /// Factory building the per-connection process bend-floor provider for one routing pass
    /// (wired by <c>MainViewModel</c>, issue #937). Invoked on the UI thread at pass start —
    /// together with the canvas-wide floor refresh — so the returned provider closes over
    /// pass-start snapshots of the component library and PDK drafts and the routing thread
    /// never enumerates live ViewModel collections. A null factory (or a null provider)
    /// clears <see cref="WaveguideRouter.ConnectionProcessFloorProvider"/> and the
    /// canvas-wide floor governs every connection.
    /// </summary>
    public Func<Func<PhysicalPin, PhysicalPin, double?>?>? BuildConnectionProcessFloorProvider { get; set; }

    /// <summary>
    /// Raised when IsRouting or RoutingStatusText changes.
    /// </summary>
    public event Action? StateChanged;

    /// <summary>Canvas-owned publication boundary for canonical group geometry.</summary>
    internal Action<IEnumerable<WaveguideConnectionViewModel>>? RouteChangesPublisher { private get; set; }

    /// <summary>Publishes one accepted routing frame to the connection views.</summary>
    internal void NotifyRouteChanges(IEnumerable<WaveguideConnectionViewModel>? changed = null)
    {
        var connections = changed ?? _connections;
        if (RouteChangesPublisher is { } publish)
        {
            publish(connections);
            return;
        }
        foreach (var connection in connections) connection.NotifyPathChanged();
    }

    /// <summary>
    /// Initializes the routing orchestrator.
    /// </summary>
    public RoutingOrchestrator(
        WaveguideRouter router,
        WaveguideConnectionManager connectionManager,
        ObservableCollection<ComponentViewModel> components,
        ObservableCollection<WaveguideConnectionViewModel> connections,
        DocumentSession? document = null)
    {
        _router = router;
        _connectionManager = connectionManager;
        _components = components;
        _connections = connections;
        _document = document ?? new DocumentSession();
    }

    /// <summary>Executes a routing calculation away from the owning UI thread.</summary>
    protected virtual Task<T> RunOnWorkerAsync<T>(Func<T> work) => Task.Run(work);

    /// <summary>Provides the worker only its captured input, scalar progress mailbox and cancellation.</summary>
    protected virtual Task<RoutingPassResult?> RunSnapshotAsync(RoutingInputSnapshot input,
        RoutingProgressMailbox progress, CancellationToken token, bool calculateRoutes = true) =>
        RunOnWorkerAsync(() => RoutingSnapshotWorker.Run(input, progress, token, calculateRoutes));

    /// <summary>Queues one routing status or result notification on the owning UI thread.</summary>
    protected virtual void PostUiUpdate(Action update) => global::Avalonia.Threading.Dispatcher.UIThread.Post(update,
        global::Avalonia.Threading.DispatcherPriority.Normal);

    /// <summary>
    /// Initializes the A* pathfinding grid with default bounds.
    /// </summary>
    public void InitializeAStarRouting()
    {
        if (_router.PathfindingGrid != null)
            _router.PathfindingGrid.ObstaclePaddingMicrometers = ComponentClearanceMicrometers;

        _router.InitializePathfindingGrid(
            DefaultGridMinX, DefaultGridMinY,
            DefaultGridMaxX, DefaultGridMaxY,
            _components.Select(c => c.Component));

        if (_router.PathfindingGrid != null)
            _router.PathfindingGrid.ObstaclePaddingMicrometers = ComponentClearanceMicrometers;
    }

    /// <summary>
    /// Reinitializes the A* pathfinding grid with custom bounds.
    /// </summary>
    public void InitializeAStarRouting(double minX, double minY, double maxX, double maxY)
    {
        _router.InitializePathfindingGrid(
            minX, minY, maxX, maxY,
            _components.Select(c => c.Component));

        if (_router.PathfindingGrid != null)
            _router.PathfindingGrid.ObstaclePaddingMicrometers = ComponentClearanceMicrometers;
    }

    private int _gridRequest;

    /// <summary>
    /// Re-initializes the A* grid for new bounds and routing regions off the UI thread — the
    /// grid of a large die carrier takes around a second to build, far beyond the 100 ms an
    /// interaction may block. The component positions are captured now; the grid is swapped in
    /// under the routing lock, so a routing pass started meanwhile waits for it, and a request
    /// superseded by a newer one is skipped.
    /// </summary>
    public Task ReinitializeAStarRoutingInBackgroundAsync(double minX, double minY, double maxX, double maxY,
        IReadOnlyList<(double X, double Y, double Width, double Height)>? regions)
        => ReinitializeAStarRoutingInBackgroundAsync(minX, minY, maxX, maxY, regions, null, null);

    /// <summary>Rebuilds derived grid state under a caller-owned edit, without recapturing its document.</summary>
    internal async Task ReinitializeAStarRoutingInBackgroundAsync(double minX, double minY, double maxX, double maxY,
        IReadOnlyList<(double X, double Y, double Width, double Height)>? regions,
        DocumentOperation? suppliedOperation, ICommitPermission? permission)
    {
        await using var ownedOperation = suppliedOperation == null ? _document.CaptureOperation() : null;
        var operation = suppliedOperation ?? ownedOperation!;
        await using var linked = new AsyncCancellationScope(operation.CancellationToken,
            permission?.CancellationToken ?? default);
        var token = linked.Token;
        int request = Interlocked.Increment(ref _gridRequest);
        try { await _routingSemaphore.WaitAsync(token); }
        catch (OperationCanceledException) { return; }
        try
        {
            while (operation.IsCurrent && request == _gridRequest)
            {
                var revision = _document.CaptureRevision();
                var input = CaptureInput().WithGrid(minX, minY, maxX, maxY, regions, ComponentClearanceMicrometers);
                var progress = new RoutingProgressMailbox();
                var result = await RunSnapshotAsync(input, progress, token, calculateRoutes: false);
                if (result == null || !operation.IsCurrent || request != _gridRequest) return;
                // A borrowed owner can advance itself during computation. That does not make
                // geometry captured before its latest edit safe to publish. Rebuild its latest
                // input; a foreign edit instead revokes the operation and ends this job.
                if (!_document.IsCurrentRevision(revision)) continue;
                var prepared = result.Prepare(_components.Select(view => view.Component).ToArray(), _connectionManager, _router);
                bool published = false;
                void Publish() => operation.TryPublish(() =>
                {
                    if (request != _gridRequest || !_document.IsCurrentRevision(revision)) return;
                    prepared.Apply();
                    published = true;
                });
                if (permission == null) Publish(); else permission.Commit(Publish);
                if (published) return;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { _routingSemaphore.Release(); }
    }

    /// <summary>Queues the ordinary group-edit grid before the next routing pass.</summary>
    public Task ReinitializeDefaultRoutingInBackgroundAsync() => ReinitializeAStarRoutingInBackgroundAsync(
        DefaultGridMinX, DefaultGridMinY, DefaultGridMaxX, DefaultGridMaxY, null);

    /// <summary>
    /// Confines routes to the given rectangles (the dies of an assembly: a waveguide never
    /// runs across the carrier between dies), or lifts the restriction with null. Takes effect
    /// with the next grid initialization or routing pass.
    /// </summary>
    public void SetAllowedRoutingRegions(IReadOnlyList<(double X, double Y, double Width, double Height)>? regions) =>
        _router.AllowedRoutingRegions = regions;

    /// <summary>
    /// Cancels the currently running routing pass (status-bar Stop button). Already-routed
    /// connections keep their new routes; the pass reports how far it got.
    /// </summary>
    public void CancelRouting()
    {
        // Revoke this pass before observers run. An observer may finish this worker
        // or start a replacement, which must not be cancelled by this Stop request.
        var current = _routingCts;
        if (current != null) _ = current.CancelAsync();
        StopRequested?.Invoke();
    }

    /// <summary>Raised for an explicit Stop, including a batch spanning several passes.</summary>
    public event Action? StopRequested;

    /// <summary>
    /// Asynchronously recalculates all waveguide routes on a background thread.
    /// Cancels any previous in-progress routing. Provides progressive updates throttled to 10 Hz.
    /// </summary>
    public Task RecalculateRoutesAsync() => RecalculateRoutesAsync(null);

    /// <summary>Routes within an existing operation when supplied; the caller retains its lifetime.</summary>
    public async Task RecalculateRoutesAsync(DocumentOperation? operation, ICommitPermission? permission = null,
        CancellationToken cancellationToken = default)
    {
        if (_suspendRouting > 0) return;
        await using var ownedOperation = operation == null ? _document.CaptureOperation(cancellationToken) : null;
        operation ??= ownedOperation!;
        if (_routingCts != null) _ = _routingCts.CancelAsync();
        using var stop = new CancellationTokenSource();
        _routingCts = stop;
        var stopToken = stop.Token;
        await using var linked = new AsyncCancellationScope(stopToken, operation.CancellationToken,
            cancellationToken, permission?.CancellationToken ?? default);
        var token = linked.Token;
        var generation = MutationGeneration;
        var passId = ++_passId;
        try { await _routingSemaphore.WaitAsync(token); }
        catch (OperationCanceledException) { if (ReferenceEquals(_routingCts, stop)) _routingCts = null; return; }
        try
        {
            if (stopToken.IsCancellationRequested || token.IsCancellationRequested
                || !operation.IsCurrent || generation != MutationGeneration) return;
            RefreshProcessSettings();
            var input = CaptureInput();
            var originalPaths = RoutingPassResult.CaptureOriginalPaths(_connectionManager);
            var progress = new RoutingProgressMailbox();
            var views = _connections.ToDictionary(view => view.Connection.Id);
            _activePassId = passId;
            _totalCount = _connectionManager.Connections.Count;
            _routedCount = 0;
            _passStartUtc = DateTime.UtcNow;
            IsRouting = true;
            RoutingStatusText = BuildProgressText();
            StateChanged?.Invoke();
            bool Current() => passId == _passId && generation == MutationGeneration && operation.IsCurrent
                && !stopToken.IsCancellationRequested && !token.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            using var heartbeat = new RoutingProgressHeartbeat(Current, BuildProgressText,
                text => { RoutingStatusText = text; StateChanged?.Invoke(); });

            // Worker input contains values, a value mailbox and cancellation; no editor objects or UI callbacks.
            var worker = RunSnapshotAsync(input, progress, token);
            while (!worker.IsCompleted)
            {
                await Task.WhenAny(worker, Task.Delay(100));
                if (!Current()) continue;
                var frame = progress.TakeFrame();
                var count = progress.RoutedCount;
                var phase = progress.Phase;
                var complex = progress.Complex;
                PostUiUpdate(() =>
                {
                    if (!Current() || !IsRouting) return;
                    _routedCount = count;
                    heartbeat.PhaseChanged(phase);
                    if (complex) heartbeat.ComplexRouteStarted(); else heartbeat.ConnectionCompleted();
                    if (frame.Count == 0) return;
                    var prepared = frame.Prepare(_connectionManager);
                    Commit(operation, permission, token, cancellationToken, stopToken, () =>
                    {
                        prepared.Apply();
                        NotifyRouteChanges(prepared.ConnectionIds.Where(views.ContainsKey).Select(id => views[id]));
                        RepaintRequested?.Invoke();
                    });
                });
            }
            var result = await worker; // Drain even after cancellation/revocation.
            if (passId == _passId) _routedCount = progress.RoutedCount;
            if (result != null && Current())
            {
                heartbeat.ConnectionCompleted();
                var prepared = result.Prepare(_components.Select(view => view.Component).ToArray(), _connectionManager, _router, originalPaths);
                if (Commit(operation, permission, token, cancellationToken, stopToken, () =>
                {
                    prepared.Apply();
                    PublishCrossings(prepared);
                    RestoreLengthTargets(originalPaths);
                    NotifyRouteChanges();
                    RepaintRequested?.Invoke();
                }))
                {
                    RoutingStatusText = "";
                    StateChanged?.Invoke();
                }
            }
            else if (passId == _passId && operation.IsCurrent)
            {
                RoutingStatusText = string.Format(LocalizationService.Instance.Translate("Routing.Status.Stopped"),
                    Math.Min(_routedCount, _totalCount), _totalCount);
                StateChanged?.Invoke();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || !operation.IsCurrent) { }
        finally
        {
            _routingSemaphore.Release();
            if (ReferenceEquals(_routingCts, stop)) _routingCts = null;
            if (_activePassId == passId) { IsRouting = false; StateChanged?.Invoke(); }
        }
    }

    /// <summary>
    /// A pass may have replaced a length-matched meander (an endpoint moved, a component now
    /// overlaps it): stretch those routes back to their recorded target, clear of the routes
    /// just committed. Only routes this pass replaced are tried — one that already failed at
    /// unchanged geometry would fail again on every pass. A target that cannot be met stays
    /// visible: its padlock turns to the warning colour and the design checks list it.
    /// </summary>
    private void RestoreLengthTargets(IReadOnlyDictionary<Guid, RoutedPath?> originalPaths)
    {
        var connections = _connectionManager.Connections;
        if (connections.TrueForAll(ConnectionLengthMatcher.MeetsTarget))
            return;
        var components = _components.Select(view => view.Component).ToArray();
        new ConnectionLengthMatcher(_router).ReapplyUnmetTargets(connections, components,
            c => !originalPaths.TryGetValue(c.Id, out var before) || !ReferenceEquals(before, c.RoutedPath));
    }

    private RoutingInputSnapshot CaptureInput() => RoutingInputSnapshot.Capture(
        _components.Select(view => view.Component).ToArray(), _router, _connectionManager, preserveCachedGrid: false);

    private void RefreshProcessSettings()
    {
        if (GetProcessMinBendRadiusMicrometers != null)
            _router.ProcessMinBendRadiusMicrometers = GetProcessMinBendRadiusMicrometers();
        if (GetMetalRoutingSpec != null)
        {
            var metal = GetMetalRoutingSpec();
            _router.MetalProcessMinBendRadiusMicrometers = metal.MinBendRadiusMicrometers;
            _connectionManager.MetalTraceWidthMicrometers = metal.TraceWidthMicrometers;
        }
        _router.ConnectionProcessFloorProvider = BuildConnectionProcessFloorProvider?.Invoke();
    }

    private static bool Commit(DocumentOperation operation, ICommitPermission? permission,
        CancellationToken token, CancellationToken caller, CancellationToken stop, Action change)
    {
        if (token.IsCancellationRequested || caller.IsCancellationRequested || stop.IsCancellationRequested) return false;
        var applied = false;
        try
        {
            void Publish()
            {
                token.ThrowIfCancellationRequested();
                caller.ThrowIfCancellationRequested();
                stop.ThrowIfCancellationRequested();
                applied = operation.TryCommitAndAdvance(() =>
                {
                    token.ThrowIfCancellationRequested();
                    caller.ThrowIfCancellationRequested();
                    stop.ThrowIfCancellationRequested();
                    change();
                });
            }
            if (permission == null) Publish(); else permission.Commit(Publish);
        }
        catch (OperationCanceledException) { return false; } // Revoked queued dispatch is an ordinary stale result.
        return applied;
    }

    private void PublishCrossings(PreparedRoutingCommit prepared)
    {
        if (_connectionManager.CrossingService is not { } service) return;
        if (prepared.CrossingChange != null) service.ChangeRecorded?.Invoke(prepared.CrossingChange);
        foreach (var crossing in prepared.RemovedCrossings) service.ComponentRemoved?.Invoke(crossing);
        foreach (var crossing in prepared.AddedCrossings) service.ComponentAdded?.Invoke(crossing);
    }

    /// <summary>
    /// Localized "Routing n/m connections · t s" status for the current pass. Retried
    /// ordering attempts can route a connection twice, so the displayed count is clamped
    /// to the connection total.
    /// </summary>
    private string BuildProgressText()
    {
        var elapsedSeconds = (int)(DateTime.UtcNow - _passStartUtc).TotalSeconds;
        return string.Format(
            LocalizationService.Instance.Translate("Routing.Status.Progress"),
            Math.Min(_routedCount, _totalCount), _totalCount, elapsedSeconds);
    }
}
