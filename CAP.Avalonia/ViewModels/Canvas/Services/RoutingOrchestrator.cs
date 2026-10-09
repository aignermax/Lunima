using System.Collections.ObjectModel;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Components;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;

namespace CAP.Avalonia.ViewModels.Canvas.Services;

/// <summary>
/// Manages asynchronous waveguide route calculation with cancellation, throttling, and grid initialization.
/// </summary>
public class RoutingOrchestrator
{
    private readonly WaveguideRouter _router;
    private readonly WaveguideConnectionManager _connectionManager;
    private readonly ObservableCollection<ComponentViewModel> _components;
    private readonly ObservableCollection<WaveguideConnectionViewModel> _connections;

    private CancellationTokenSource? _routingCts;
    private readonly SemaphoreSlim _routingSemaphore = new(1, 1);

    private int _routedCount;
    private int _totalCount;
    private int _passId;
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

    /// <summary>
    /// Initializes the routing orchestrator.
    /// </summary>
    public RoutingOrchestrator(
        WaveguideRouter router,
        WaveguideConnectionManager connectionManager,
        ObservableCollection<ComponentViewModel> components,
        ObservableCollection<WaveguideConnectionViewModel> connections)
    {
        _router = router;
        _connectionManager = connectionManager;
        _components = components;
        _connections = connections;
    }

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

    /// <summary>
    /// Cancels the currently running routing pass (status-bar Stop button). Already-routed
    /// connections keep their new routes; the pass reports how far it got.
    /// </summary>
    public void CancelRouting() => _routingCts?.Cancel();

    /// <summary>
    /// Asynchronously recalculates all waveguide routes on a background thread.
    /// Cancels any previous in-progress routing. Provides progressive updates throttled to 10 Hz.
    /// </summary>
    public async Task RecalculateRoutesAsync()
    {
        // Refresh the process bend-radius floor before every pass (still on the caller's
        // UI thread — the provider reads ViewModel state).
        if (GetProcessMinBendRadiusMicrometers != null)
            _router.ProcessMinBendRadiusMicrometers = GetProcessMinBendRadiusMicrometers();
        if (GetMetalRoutingSpec != null)
        {
            var metalSpec = GetMetalRoutingSpec();
            _router.MetalProcessMinBendRadiusMicrometers = metalSpec.MinBendRadiusMicrometers;
            _connectionManager.MetalTraceWidthMicrometers = metalSpec.TraceWidthMicrometers;
        }
        // Per-connection floor (issue #937): the factory runs here on the UI thread, so the
        // provider it builds can close over pass-start snapshots; the router then consults
        // it per connection on the routing thread.
        _router.ConnectionProcessFloorProvider = BuildConnectionProcessFloorProvider?.Invoke();

        _routingCts?.Cancel();
        _routingCts?.Dispose();
        _routingCts = new CancellationTokenSource();
        var token = _routingCts.Token;

        try
        {
            await _routingSemaphore.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (token.IsCancellationRequested) return;

            var passId = ++_passId;
            _totalCount = _connectionManager.Connections.Count;
            _routedCount = 0;
            _passStartUtc = DateTime.UtcNow;

            IsRouting = true;
            RoutingStatusText = BuildProgressText();
            StateChanged?.Invoke();

            // Wire Phase 2 callback: update status text when a complex route is being computed.
            // Called on the background routing thread — must post to UI thread.
            _connectionManager.OnComplexRouteStarted = () =>
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (passId == _passId && !token.IsCancellationRequested)
                    {
                        RoutingStatusText = LocalizationService.Instance.Translate("Routing.Status.ComplexPath");
                        StateChanged?.Invoke();
                    }
                }, global::Avalonia.Threading.DispatcherPriority.Normal);
            };

            var components = _components.Select(c => c.Component).ToList();
            var lastUpdateTime = DateTime.MinValue;
            var updateLock = new object();

            Action progressCallback = () =>
            {
                var routed = Interlocked.Increment(ref _routedCount);
                lock (updateLock)
                {
                    var now = DateTime.UtcNow;
                    // The last connection always forces an update so the counter visibly
                    // reaches total/total; intermediate updates stay throttled to 10 Hz.
                    var isFinal = routed >= _totalCount;
                    if (!isFinal && (now - lastUpdateTime).TotalMilliseconds < 100)
                        return;

                    lastUpdateTime = now;
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (passId != _passId || token.IsCancellationRequested || !IsRouting)
                            return;

                        RoutingStatusText = BuildProgressText();
                        StateChanged?.Invoke();
                        foreach (var conn in _connections)
                            conn.NotifyPathChanged();
                        RepaintRequested?.Invoke();
                    }, global::Avalonia.Threading.DispatcherPriority.Normal);
                }
            };

            var completed = await Task.Run(() =>
            {
                if (token.IsCancellationRequested) return false;
                _router.PathfindingGrid?.RebuildFromComponents(components);
                if (token.IsCancellationRequested) return false;
                _connectionManager.RecalculateAllTransmissions(progressCallback, token);
                return !token.IsCancellationRequested;
            });

            if (completed && !token.IsCancellationRequested)
            {
                foreach (var conn in _connections)
                    conn.NotifyPathChanged();
                RoutingStatusText = "";
                StateChanged?.Invoke();
            }
            else
            {
                // Stopped (Stop button, or superseded by a newer pass which immediately
                // overwrites this): already-routed wires keep their routes, the rest stay
                // as they were — report how far the pass got.
                RoutingStatusText = string.Format(
                    LocalizationService.Instance.Translate("Routing.Status.Stopped"),
                    Math.Min(_routedCount, _totalCount), _totalCount);
                StateChanged?.Invoke();
            }
        }
        finally
        {
            _connectionManager.OnComplexRouteStarted = null;
            _routingSemaphore.Release();
            IsRouting = false;
            StateChanged?.Invoke();
        }
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
