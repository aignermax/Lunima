using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using CAP.Avalonia.Selection;
using CAP.Avalonia.Visualization;
using CAP.Avalonia.ViewModels.Simulation;

namespace CAP.Avalonia.ViewModels.Canvas;

public partial class DesignCanvasViewModel : ObservableObject
{
    public ObservableCollection<ComponentViewModel> Components { get; } = new();
    public ObservableCollection<WaveguideConnectionViewModel> Connections { get; } = new();
    public ObservableCollection<PinViewModel> AllPins { get; } = new();

    public WaveguideConnectionManager ConnectionManager { get; } = new();

    /// <summary>
    /// Manages multi-component selection state.
    /// </summary>
    public SelectionManager Selection { get; } = new();

    /// <summary>
    /// Clipboard for copy-paste operations.
    /// </summary>
    public ComponentClipboard Clipboard { get; } = new();

    /// <summary>
    /// Manages power flow visualization state and data.
    /// </summary>
    public PowerFlowVisualizer PowerFlowVisualizer { get; } = new();

    /// <summary>
    /// Manages pin alignment guide visualization during component dragging.
    /// </summary>
    public AlignmentGuideViewModel AlignmentGuide { get; } = new();

    /// <summary>
    /// Whether power flow overlay is currently visible.
    /// </summary>
    [ObservableProperty]
    private bool _showPowerFlow;

    /// <summary>
    /// Callback invoked when simulation needs to be re-run (e.g., circuit changed while overlay is active).
    /// Set by MainViewModel to trigger auto-recalculation.
    /// </summary>
    public Action? SimulationRequested { get; set; }

    /// <summary>
    /// Callback invoked when the canvas needs to be repainted (e.g., during incremental routing updates).
    /// Set by the DesignCanvas control to trigger InvalidateVisual().
    /// </summary>
    public Action? RepaintRequested { get; set; }

    /// <summary>
    /// Called when the circuit topology changes (component moved, connection added/removed).
    /// If the power overlay is active, requests auto-recalculation instead of hiding it.
    /// </summary>
    public void InvalidateSimulation()
    {
        bool wasShowingOverlay = ShowPowerFlow;
        PowerFlowVisualizer.Clear();
        ShowPowerFlow = false;

        if (wasShowingOverlay)
        {
            // Overlay was active - auto-recalculate (SimulationService will re-enable ShowPowerFlow)
            SimulationRequested?.Invoke();
        }
    }

    /// <summary>
    /// Called when a component parameter (slider) changes.
    /// Re-runs simulation without clearing the overlay, avoiding visual flicker.
    /// </summary>
    public void RequestResimulation()
    {
        if (ShowPowerFlow)
            SimulationRequested?.Invoke();
    }

    /// <summary>
    /// Updates the power flow display after simulation.
    /// If overlay is already visible, forces a re-render without toggling off/on.
    /// </summary>
    public void RefreshPowerFlowDisplay()
    {
        PowerFlowVisualizer.IsEnabled = true;
        if (ShowPowerFlow)
        {
            // Already visible — just force re-render with updated data
            OnPropertyChanged(nameof(ShowPowerFlow));
        }
        else
        {
            ShowPowerFlow = true;
        }
    }

    /// <summary>
    /// The currently highlighted pin (when mouse is near in Connect mode).
    /// </summary>
    [ObservableProperty]
    private PinViewModel? _highlightedPin;

    /// <summary>
    /// Distance threshold for pin highlighting (in micrometers).
    /// </summary>
    public double PinHighlightDistance { get; set; } = 15.0;

    /// <summary>
    /// Gets the shared waveguide router for A* pathfinding configuration.
    /// </summary>
    public WaveguideRouter Router => WaveguideConnection.SharedRouter;

    /// <summary>
    /// Whether A* pathfinding with obstacle avoidance is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _useAStarRouting = true;

    /// <summary>
    /// Whether to show the pathfinding grid overlay for debugging.
    /// Shows blocked cells (components in red, waveguides in blue).
    /// </summary>
    [ObservableProperty]
    private bool _showGridOverlay = false;

    /// <summary>
    /// Settings for optional grid snapping during component placement and drag.
    /// </summary>
    public GridSnapSettings GridSnap { get; } = new();

    /// <summary>
    /// Minimum bend radius for waveguides in micrometers.
    /// Depends on the fabrication process - typical values: 5-20µm for silicon photonics.
    /// Default: 10µm (conservative value for most foundries).
    /// </summary>
    [ObservableProperty]
    private double _minBendRadiusMicrometers = 10.0;

    partial void OnMinBendRadiusMicrometersChanged(double value)
    {
        Router.MinBendRadiusMicrometers = value;
        // Recalculate all waveguide routes with new bend radius (async)
        _ = RecalculateRoutesAsync();
    }

    /// <summary>
    /// Chip boundary in micrometers. Components can only be placed within this area.
    /// Default: 5mm × 5mm (typical MPW chip size)
    /// </summary>
    public double ChipMinX { get; set; } = 0;
    public double ChipMinY { get; set; } = 0;
    public double ChipMaxX { get; set; } = 5000;
    public double ChipMaxY { get; set; } = 5000;

    /// <summary>
    /// Default canvas bounds for A* pathfinding grid (in micrometers).
    /// Slightly larger than chip to allow routing near edges.
    /// </summary>
    private const double DefaultGridMinX = -100;
    private const double DefaultGridMinY = -100;
    private const double DefaultGridMaxX = 5100;
    private const double DefaultGridMaxY = 5100;

    /// <summary>
    /// Minimum clearance between waveguides and components (in micrometers).
    /// </summary>
    private const double ComponentClearanceMicrometers = 5.0;

    private bool _isDragging;
    private bool _isExecutingCommand; // True during command Execute/Undo to skip collision checks
    private CancellationTokenSource? _routingCts;
    private readonly SemaphoreSlim _routingSemaphore = new(1, 1);

    [ObservableProperty]
    private bool _isRouting;

    [ObservableProperty]
    private string _routingStatusText = "";

    [ObservableProperty]
    private ComponentViewModel? _selectedComponent;

    [ObservableProperty]
    private double _panX;

    [ObservableProperty]
    private double _panY;

    public DesignCanvasViewModel()
    {
        // Initialize A* pathfinding by default
        InitializeAStarRouting();
    }

    /// <summary>
    /// Initializes the A* pathfinding grid with default bounds.
    /// Call this before adding components if using A* routing.
    /// </summary>
    public void InitializeAStarRouting()
    {
        // Set obstacle padding for clearance between waveguides and components
        if (Router.PathfindingGrid != null)
        {
            Router.PathfindingGrid.ObstaclePaddingMicrometers = ComponentClearanceMicrometers;
        }

        Router.InitializePathfindingGrid(
            DefaultGridMinX, DefaultGridMinY,
            DefaultGridMaxX, DefaultGridMaxY,
            Components.Select(c => c.Component));

        // Apply padding after initialization
        if (Router.PathfindingGrid != null)
        {
            Router.PathfindingGrid.ObstaclePaddingMicrometers = ComponentClearanceMicrometers;
        }

        // NOTE: HPA* hierarchical pathfinding is available but not activated yet.
        // Router.BuildHierarchicalGraph() creates sector portals that force suboptimal
        // detours, and the DistanceTransform goes stale during sequential routing
        // (disabling proximity cost). Needs incremental DT updates before activation.
    }

    /// <summary>
    /// Reinitializes the A* pathfinding grid with custom bounds.
    /// </summary>
    public void InitializeAStarRouting(double minX, double minY, double maxX, double maxY)
    {
        Router.InitializePathfindingGrid(
            minX, minY, maxX, maxY,
            Components.Select(c => c.Component));

        // Apply padding after initialization
        if (Router.PathfindingGrid != null)
        {
            Router.PathfindingGrid.ObstaclePaddingMicrometers = ComponentClearanceMicrometers;
        }
    }

    partial void OnUseAStarRoutingChanged(bool value)
    {
        // A* is always used; this toggle is kept for backward compatibility.
        // Recalculate connections (async)
        _ = RecalculateRoutesAsync();
    }

    /// <summary>
    /// Call when starting to drag a component. Disables expensive recalculations.
    /// </summary>
    public void BeginDragComponent(ComponentViewModel component)
    {
        _isDragging = true;
    }

    /// <summary>
    /// Called before executing a command (Execute or Undo) to disable collision checking.
    /// This allows components to move freely during undo/redo operations.
    /// </summary>
    public void BeginCommandExecution()
    {
        _isExecutingCommand = true;
    }

    /// <summary>
    /// Called after executing a command (Execute or Undo) to re-enable collision checking.
    /// </summary>
    public void EndCommandExecution()
    {
        _isExecutingCommand = false;
    }

    /// <summary>
    /// Call when done dragging a component. Triggers async route recalculation.
    /// </summary>
    public async void EndDragComponent(ComponentViewModel component)
    {
        _isDragging = false;

        // Recalculate routes asynchronously so UI stays responsive.
        // RecalculateRoutesAsync rebuilds the obstacle grid internally
        // (inside the semaphore, on the background thread) to prevent races.
        await RecalculateRoutesAsync();
        InvalidateSimulation();
    }

    /// <summary>
    /// Asynchronously recalculates all waveguide routes on a background thread.
    /// Cancels any previous in-progress routing. Updates UI on completion.
    /// Always rebuilds the obstacle grid before routing to ensure consistency.
    /// Provides progressive visual updates throttled to max 10 Hz (every 100ms).
    /// </summary>
    public async Task RecalculateRoutesAsync()
    {
        // Cancel any previous routing operation
        _routingCts?.Cancel();
        _routingCts?.Dispose();
        _routingCts = new CancellationTokenSource();
        var token = _routingCts.Token;

        // Serialize routing: wait for any in-progress routing to finish.
        // Pass the token so if this operation is cancelled while waiting, it bails immediately.
        try
        {
            await _routingSemaphore.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            // This routing request was superseded by a newer one
            return;
        }

        try
        {
            // Double-check: if our token was cancelled while waiting, skip routing
            if (token.IsCancellationRequested) return;

            IsRouting = true;
            RoutingStatusText = $"Routing {ConnectionManager.Connections.Count} connections...";

            // Snapshot component list on UI thread for thread safety
            var components = Components.Select(c => c.Component).ToList();

            // Throttle UI updates to max 10 Hz (every 100ms)
            var lastUpdateTime = DateTime.MinValue;
            var updateLock = new object();
            bool updatePending = false;

            Action progressCallback = () =>
            {
                lock (updateLock)
                {
                    var now = DateTime.UtcNow;
                    var elapsed = (now - lastUpdateTime).TotalMilliseconds;

                    // Throttle: only update if at least 100ms have passed
                    if (elapsed >= 100)
                    {
                        lastUpdateTime = now;
                        updatePending = false;

                        // Dispatch to UI thread with Normal priority (not Background) to ensure updates are visible
                        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested)
                            {
                                // Notify all connections to update their paths
                                foreach (var conn in Connections)
                                {
                                    conn.NotifyPathChanged();
                                }
                                // Request canvas repaint to make changes visible
                                RepaintRequested?.Invoke();
                            }
                        }, global::Avalonia.Threading.DispatcherPriority.Normal);
                    }
                    else
                    {
                        updatePending = true;
                    }
                }
            };

            var completed = await Task.Run(() =>
            {
                if (token.IsCancellationRequested) return false;

                // Rebuild obstacle grid inside semaphore + background thread.
                // This prevents races: RebuildFromComponents and routing
                // always execute on the same thread, never concurrently.
                Router.PathfindingGrid?.RebuildFromComponents(components);

                if (token.IsCancellationRequested) return false;

                ConnectionManager.RecalculateAllTransmissions(progressCallback, token);
                return !token.IsCancellationRequested;
            });

            if (completed && !token.IsCancellationRequested)
            {
                // Final update to ensure all paths are shown
                foreach (var conn in Connections)
                {
                    conn.NotifyPathChanged();
                }
                RoutingStatusText = "";
            }
        }
        finally
        {
            _routingSemaphore.Release();
            IsRouting = false;
        }
    }

    /// <summary>
    /// Connects two pins asynchronously, routing on a background thread.
    /// Used for interactive connection creation (UI stays responsive).
    /// </summary>
    public async Task<WaveguideConnectionViewModel?> ConnectPinsAsync(
        PhysicalPin startPin, PhysicalPin endPin)
    {
        RemoveConnectionsForPin(startPin);
        RemoveConnectionsForPin(endPin);

        var connection = ConnectionManager.AddConnectionDeferred(startPin, endPin);
        var vm = new WaveguideConnectionViewModel(connection);
        Connections.Add(vm);

        await RecalculateRoutesAsync();
        InvalidateSimulation();
        return vm;
    }

    /// <summary>
    /// Checks if a component can be placed at the given position without overlapping others.
    /// </summary>
    public bool CanMoveComponentTo(ComponentViewModel component, double x, double y)
    {
        // For ComponentGroups, use specialized collision detection
        if (component.Component is ComponentGroup group)
        {
            return CanMoveGroupTo(group, x, y, excludeComponent: component);
        }

        return CanPlaceComponent(x, y, component.Width, component.Height, excludeComponent: component);
    }

    /// <summary>
    /// Checks if a ComponentGroup can be placed at the given position.
    /// Uses bounding-box collision detection against other components.
    /// </summary>
    private bool CanMoveGroupTo(ComponentGroup group, double x, double y, ComponentViewModel? excludeComponent = null)
    {
        if (x < ChipMinX || y < ChipMinY ||
            x + group.WidthMicrometers > ChipMaxX ||
            y + group.HeightMicrometers > ChipMaxY)
        {
            return false;
        }

        // Build set of child components to exclude from collision checks
        var childSet = new HashSet<Component>(group.GetAllComponentsRecursive());
        if (excludeComponent != null)
        {
            childSet.Add(excludeComponent.Component);
        }

        // Check each child component against non-group components
        foreach (var child in group.ChildComponents)
        {
            var testRect = new Rect(
                child.PhysicalX - MinComponentGapMicrometers,
                child.PhysicalY - MinComponentGapMicrometers,
                child.WidthMicrometers + MinComponentGapMicrometers * 2,
                child.HeightMicrometers + MinComponentGapMicrometers * 2);

            foreach (var comp in Components)
            {
                if (comp == excludeComponent) continue;
                if (childSet.Contains(comp.Component)) continue;

                var compRect = new Rect(comp.X, comp.Y, comp.Width, comp.Height);
                if (RectsOverlap(testRect, compRect))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public void MoveComponent(ComponentViewModel component, double deltaX, double deltaY)
    {
        // Don't move locked components
        if (component.Component.IsLocked)
            return;

        // If this is a ComponentGroup, use specialized group movement
        if (component.Component is ComponentGroup group)
        {
            MoveComponentGroup(component, group, deltaX, deltaY);
            return;
        }

        double newX = component.X + deltaX;
        double newY = component.Y + deltaY;

        // During drag or command execution: allow free movement (collision checked on drop or not applicable for undo/redo)
        // Otherwise: check for overlap
        if (!_isDragging && !_isExecutingCommand && !CanPlaceComponent(newX, newY, component.Width, component.Height, excludeComponent: component))
        {
            return;
        }

        component.X = newX;
        component.Y = newY;

        // Update the underlying component
        component.Component.PhysicalX = component.X;
        component.Component.PhysicalY = component.Y;

        if (_isDragging)
        {
            // During drag: only update UI positions, no expensive routing
            // Just notify that pin positions changed so the UI can redraw connection endpoints
            foreach (var conn in Connections)
            {
                if (conn.Connection.StartPin.ParentComponent == component.Component ||
                    conn.Connection.EndPin.ParentComponent == component.Component)
                {
                    conn.NotifyPathChanged();
                }
            }
        }
        else
        {
            // Not dragging (e.g., programmatic move, undo/redo): route async
            // RecalculateRoutesAsync will rebuild the obstacle grid inside the semaphore,
            // preventing threading issues from concurrent undo/redo operations
            _ = RecalculateRoutesAsync();
        }
    }

    /// <summary>
    /// Moves a ComponentGroup and all its children together.
    /// Uses ComponentGroup.MoveGroup() to maintain internal consistency.
    /// </summary>
    private void MoveComponentGroup(ComponentViewModel component, ComponentGroup group, double deltaX, double deltaY)
    {
        // Calculate new position
        double newX = component.X + deltaX;
        double newY = component.Y + deltaY;

        // During drag or command execution: allow free movement (collision checked on drop or not applicable for undo/redo)
        // Otherwise: check for overlap (check group bounds)
        if (!_isDragging && !_isExecutingCommand)
        {
            var groupBounds = CalculateGroupBounds(group);
            double testX = groupBounds.X + deltaX;
            double testY = groupBounds.Y + deltaY;

            if (!CanPlaceComponent(testX, testY, groupBounds.Width, groupBounds.Height, excludeComponent: component))
            {
                return;
            }
        }

        // Update the ComponentViewModel position
        component.X = newX;
        component.Y = newY;

        // Use the ComponentGroup's MoveGroup method to move all children and internal paths
        group.MoveGroup(deltaX, deltaY);

        // Update connections for child component pins
        if (_isDragging)
        {
            // During drag: update UI positions for all connections to child components
            var childComponents = group.GetAllComponentsRecursive();
            foreach (var conn in Connections)
            {
                if (childComponents.Contains(conn.Connection.StartPin.ParentComponent) ||
                    childComponents.Contains(conn.Connection.EndPin.ParentComponent))
                {
                    conn.NotifyPathChanged();
                }
            }
        }
        else
        {
            // Not dragging: route async
            _ = RecalculateRoutesAsync();
        }
    }

    /// <summary>
    /// Calculates the bounding rectangle for a ComponentGroup based on its children.
    /// </summary>
    private (double X, double Y, double Width, double Height) CalculateGroupBounds(ComponentGroup group)
    {
        if (group.ChildComponents.Count == 0)
        {
            return (group.PhysicalX, group.PhysicalY, group.WidthMicrometers, group.HeightMicrometers);
        }

        double minX = group.ChildComponents.Min(c => c.PhysicalX);
        double minY = group.ChildComponents.Min(c => c.PhysicalY);
        double maxX = group.ChildComponents.Max(c => c.PhysicalX + c.WidthMicrometers);
        double maxY = group.ChildComponents.Max(c => c.PhysicalY + c.HeightMicrometers);

        return (minX, minY, maxX - minX, maxY - minY);
    }

    public ComponentViewModel AddComponent(Component component, string? templateName = null)
    {
        var vm = new ComponentViewModel(component, templateName);
        vm.OnSliderChanged = () => RequestResimulation();
        Components.Add(vm);

        // Add obstacle to A* pathfinding grid
        Router.AddComponentObstacle(component);

        // Add pin ViewModels for this component
        foreach (var pin in component.PhysicalPins)
        {
            AllPins.Add(new PinViewModel(pin, vm));
        }

        return vm;
    }

    public void RemoveComponent(ComponentViewModel component)
    {
        // Remove obstacle from A* pathfinding grid
        Router.RemoveComponentObstacle(component.Component);

        ConnectionManager.RemoveConnectionsForComponent(component.Component);

        // Remove pin ViewModels for this component
        var pinsToRemove = AllPins.Where(p => p.ParentComponentViewModel == component).ToList();
        foreach (var pin in pinsToRemove)
        {
            AllPins.Remove(pin);
        }

        // Remove connection view models
        var connectionsToRemove = Connections
            .Where(c => c.Connection.StartPin.ParentComponent == component.Component ||
                        c.Connection.EndPin.ParentComponent == component.Component)
            .ToList();

        foreach (var conn in connectionsToRemove)
        {
            Connections.Remove(conn);
        }

        Components.Remove(component);

        // Recalculate remaining routes asynchronously (freed space may allow better routes)
        if (ConnectionManager.Connections.Count > 0)
        {
            _ = RecalculateRoutesAsync();
        }

        InvalidateSimulation();
    }

    /// <summary>
    /// Creates a connection between two pins without routing.
    /// Routing should be triggered separately via RecalculateRoutesAsync().
    /// Used by commands (Execute/Undo pattern) where routing is fire-and-forget.
    /// </summary>
    public WaveguideConnectionViewModel? ConnectPins(PhysicalPin startPin, PhysicalPin endPin)
    {
        // Remove any existing connections on either pin (only one waveguide per pin allowed)
        RemoveConnectionsForPin(startPin);
        RemoveConnectionsForPin(endPin);

        var connection = ConnectionManager.AddConnectionDeferred(startPin, endPin);
        var vm = new WaveguideConnectionViewModel(connection);
        Connections.Add(vm);

        InvalidateSimulation();
        return vm;
    }

    /// <summary>
    /// Connects two pins using a previously cached route, bypassing A* routing.
    /// Used when loading designs with cached route data.
    /// Does not call InvalidateSimulation or NotifyPathChanged — caller should do that after bulk loading.
    /// </summary>
    public WaveguideConnectionViewModel? ConnectPinsWithCachedRoute(
        PhysicalPin startPin,
        PhysicalPin endPin,
        RoutedPath cachedRoute)
    {
        RemoveConnectionsForPin(startPin);
        RemoveConnectionsForPin(endPin);

        var connection = ConnectionManager.AddConnectionWithCachedRoute(startPin, endPin, cachedRoute);
        var vm = new WaveguideConnectionViewModel(connection);
        Connections.Add(vm);

        return vm;
    }

    /// <summary>
    /// Removes all connections that involve a specific pin.
    /// Uses deferred removal (no re-routing). Caller is responsible for triggering routing.
    /// </summary>
    public void RemoveConnectionsForPin(PhysicalPin pin)
    {
        var connectionsToRemove = Connections
            .Where(c => c.Connection.StartPin == pin || c.Connection.EndPin == pin)
            .ToList();

        foreach (var conn in connectionsToRemove)
        {
            ConnectionManager.RemoveConnectionDeferred(conn.Connection);
            Connections.Remove(conn);
        }

        if (connectionsToRemove.Count > 0)
            InvalidateSimulation();
    }

    /// <summary>
    /// Gets the connection for a specific pin, if any.
    /// </summary>
    public WaveguideConnectionViewModel? GetConnectionForPin(PhysicalPin pin)
    {
        return Connections.FirstOrDefault(c =>
            c.Connection.StartPin == pin || c.Connection.EndPin == pin);
    }

    /// <summary>
    /// Finds and highlights the nearest pin to the given position.
    /// Called when mouse moves in Connect mode.
    /// </summary>
    /// <param name="x">Mouse X position in micrometers</param>
    /// <param name="y">Mouse Y position in micrometers</param>
    /// <param name="excludePin">Optional pin to exclude (e.g., the connection start pin)</param>
    /// <returns>The nearest pin if within highlight distance, otherwise null</returns>
    public PinViewModel? UpdatePinHighlight(double x, double y, PhysicalPin? excludePin = null)
    {
        // Clear previous highlight
        if (HighlightedPin != null)
        {
            HighlightedPin.SetHighlighted(false);
            HighlightedPin = null;
        }

        // Find nearest pin
        PinViewModel? nearest = null;
        double nearestDistance = double.MaxValue;

        foreach (var pinVm in AllPins)
        {
            // Skip excluded pin (and pins on the same component as excludePin)
            if (excludePin != null)
            {
                if (pinVm.Pin == excludePin) continue;
                if (pinVm.Pin.ParentComponent == excludePin.ParentComponent) continue;
            }

            var (pinX, pinY) = pinVm.Pin.GetAbsolutePosition();
            double dist = Math.Sqrt(Math.Pow(x - pinX, 2) + Math.Pow(y - pinY, 2));

            if (dist < nearestDistance && dist <= PinHighlightDistance)
            {
                nearest = pinVm;
                nearestDistance = dist;
            }
        }

        // Highlight the nearest pin
        if (nearest != null)
        {
            nearest.SetHighlighted(true);
            // Update whether this pin has a connection
            nearest.HasConnection = GetConnectionForPin(nearest.Pin) != null;
            HighlightedPin = nearest;
        }

        return nearest;
    }

    /// <summary>
    /// Clears all pin highlighting.
    /// </summary>
    public void ClearPinHighlight()
    {
        if (HighlightedPin != null)
        {
            HighlightedPin.SetHighlighted(false);
            HighlightedPin = null;
        }
    }

    /// <summary>
    /// Gets the nearest pin at or near the given position.
    /// Uses the same nearest-pin logic as UpdatePinHighlight for consistency.
    /// </summary>
    public PhysicalPin? GetPinAt(double x, double y, double tolerance = 15.0)
    {
        PhysicalPin? nearest = null;
        double nearestDistance = double.MaxValue;

        foreach (var pinVm in AllPins)
        {
            var (pinX, pinY) = pinVm.Pin.GetAbsolutePosition();
            double dist = Math.Sqrt(Math.Pow(x - pinX, 2) + Math.Pow(y - pinY, 2));

            if (dist < nearestDistance && dist <= tolerance)
            {
                nearest = pinVm.Pin;
                nearestDistance = dist;
            }
        }
        return nearest;
    }

    /// <summary>
    /// Minimum gap between components in micrometers.
    /// </summary>
    private const double MinComponentGapMicrometers = 5.0;

    /// <summary>
    /// Checks if a component can be placed at the given position without overlapping others
    /// and within chip boundaries.
    /// </summary>
    public bool CanPlaceComponent(double x, double y, double width, double height, ComponentViewModel? excludeComponent = null)
    {
        // Check chip boundaries
        if (x < ChipMinX || y < ChipMinY ||
            x + width > ChipMaxX || y + height > ChipMaxY)
        {
            return false;
        }

        // Add padding for minimum gap
        var testRect = new Rect(
            x - MinComponentGapMicrometers,
            y - MinComponentGapMicrometers,
            width + MinComponentGapMicrometers * 2,
            height + MinComponentGapMicrometers * 2);

        foreach (var comp in Components)
        {
            if (comp == excludeComponent) continue;

            var compRect = new Rect(comp.X, comp.Y, comp.Width, comp.Height);
            if (RectsOverlap(testRect, compRect))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Finds a valid position near the requested position if the exact position overlaps.
    /// Returns null if no valid position can be found within chip boundaries.
    /// </summary>
    public (double x, double y)? FindValidPlacement(double x, double y, double width, double height)
    {
        // Clamp initial position to chip boundaries
        x = Math.Max(ChipMinX, Math.Min(x, ChipMaxX - width));
        y = Math.Max(ChipMinY, Math.Min(y, ChipMaxY - height));

        if (CanPlaceComponent(x, y, width, height))
        {
            return (x, y);
        }

        // Try positions in expanding circles around the requested position
        double searchStep = 50; // 50µm steps
        double maxRadius = Math.Max(ChipMaxX - ChipMinX, ChipMaxY - ChipMinY);

        for (double radius = searchStep; radius <= maxRadius; radius += searchStep)
        {
            for (double angle = 0; angle < 360; angle += 30)
            {
                double testX = x + radius * Math.Cos(angle * Math.PI / 180);
                double testY = y + radius * Math.Sin(angle * Math.PI / 180);

                if (CanPlaceComponent(testX, testY, width, height))
                {
                    return (testX, testY);
                }
            }
        }

        // No valid position found
        return null;
    }

    private static bool RectsOverlap(Rect a, Rect b)
    {
        return a.X < b.X + b.Width &&
               a.X + a.Width > b.X &&
               a.Y < b.Y + b.Height &&
               a.Y + a.Height > b.Y;
    }

    private readonly struct Rect
    {
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }

        public Rect(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

}

public partial class ComponentViewModel : ObservableObject
{
    /// <summary>
    /// Template names treated as light input sources.
    /// </summary>
    private static readonly HashSet<string> LightSourceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Grating Coupler",
        "Edge Coupler"
    };

    public Component Component { get; }

    /// <summary>
    /// The name of the template used to create this component.
    /// Used for save/load to recreate the component from the correct template.
    /// </summary>
    public string? TemplateName { get; set; }

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Whether this component is locked (cannot be moved, rotated, or deleted).
    /// </summary>
    public bool IsLocked => Component.IsLocked;

    /// <summary>
    /// Notifies that the lock state has changed (called after undo/redo or direct lock/unlock).
    /// </summary>
    public void NotifyLockStateChanged()
    {
        OnPropertyChanged(nameof(IsLocked));
    }

    /// <summary>
    /// Laser configuration for light source components (null for non-sources).
    /// </summary>
    public LaserConfig? LaserConfig { get; }

    /// <summary>
    /// Whether this component is a light source (Grating/Edge Coupler).
    /// </summary>
    public bool IsLightSource => LaserConfig != null;

    public double Width => Component.WidthMicrometers;
    public double Height => Component.HeightMicrometers;
    public string Name => Component.Identifier;

    /// <summary>
    /// Whether this ComponentViewModel represents a ComponentGroup.
    /// </summary>
    public bool IsComponentGroup => Component is ComponentGroup;

    /// <summary>
    /// Whether this component has adjustable slider parameters.
    /// </summary>
    public bool HasSliders => Component.GetAllSliders().Count > 0;

    /// <summary>
    /// Display label for the slider (e.g., "Phase (°)").
    /// </summary>
    public string SliderLabel
    {
        get
        {
            if (TemplateName?.Contains("Phase") == true) return "Phase (°)";
            if (TemplateName?.Contains("Directional") == true) return "Coupling (%)";
            return "Parameter";
        }
    }

    /// <summary>
    /// First slider's current value (get/set).
    /// </summary>
    /// <summary>
    /// Callback to notify the canvas that a slider changed (triggers auto-re-simulation).
    /// Set by DesignCanvasViewModel when the component is added.
    /// </summary>
    public Action? OnSliderChanged { get; set; }

    public double SliderValue
    {
        get => Component.GetSlider(0)?.Value ?? 0;
        set
        {
            var slider = Component.GetSlider(0);
            if (slider != null && Math.Abs(slider.Value - value) > 0.001)
            {
                slider.Value = value;
                OnPropertyChanged();
                OnSliderChanged?.Invoke();
            }
        }
    }

    public double SliderMin => Component.GetSlider(0)?.MinValue ?? 0;
    public double SliderMax => Component.GetSlider(0)?.MaxValue ?? 1;

    public ComponentViewModel(Component component, string? templateName = null)
    {
        Component = component;
        TemplateName = templateName;
        _x = component.PhysicalX;
        _y = component.PhysicalY;

        if (templateName != null && LightSourceNames.Contains(templateName))
        {
            LaserConfig = new LaserConfig();
        }
    }

    public void NotifyDimensionsChanged()
    {
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
    }
}

public partial class WaveguideConnectionViewModel : ObservableObject
{
    public WaveguideConnection Connection { get; }

    [ObservableProperty]
    private bool _isSelected;

    public double StartX => Connection.StartPin.GetAbsolutePosition().x;
    public double StartY => Connection.StartPin.GetAbsolutePosition().y;
    public double EndX => Connection.EndPin.GetAbsolutePosition().x;
    public double EndY => Connection.EndPin.GetAbsolutePosition().y;

    public double PathLength => Connection.PathLengthMicrometers;
    public double LossDb => Connection.TotalLossDb;

    /// <summary>
    /// Indicates if the connection uses a fallback path that goes through obstacles.
    /// When true, the path should be displayed differently (e.g., red/dashed).
    /// </summary>
    public bool IsBlockedFallback => Connection.IsBlockedFallback;

    /// <summary>
    /// Whether target length constraint is enabled for this connection.
    /// </summary>
    public bool IsTargetLengthEnabled => Connection.IsTargetLengthEnabled;

    /// <summary>
    /// Target length in micrometers (null if not set).
    /// </summary>
    public double? TargetLengthMicrometers => Connection.TargetLengthMicrometers;

    /// <summary>
    /// Whether the current path length matches the target within tolerance.
    /// </summary>
    public bool? IsLengthMatched => Connection.IsLengthMatched;

    /// <summary>
    /// Difference between actual and target length in micrometers.
    /// </summary>
    public double? LengthDifference => Connection.LengthDifference;

    public WaveguideConnectionViewModel(WaveguideConnection connection)
    {
        Connection = connection;
    }

    public void NotifyPathChanged()
    {
        OnPropertyChanged(nameof(StartX));
        OnPropertyChanged(nameof(StartY));
        OnPropertyChanged(nameof(EndX));
        OnPropertyChanged(nameof(EndY));
        OnPropertyChanged(nameof(PathLength));
        OnPropertyChanged(nameof(LossDb));
        OnPropertyChanged(nameof(IsBlockedFallback));
        OnPropertyChanged(nameof(IsTargetLengthEnabled));
        OnPropertyChanged(nameof(TargetLengthMicrometers));
        OnPropertyChanged(nameof(IsLengthMatched));
        OnPropertyChanged(nameof(LengthDifference));
    }
}

/// <summary>
/// ViewModel for a physical pin with highlighting support.
/// </summary>
public partial class PinViewModel : ObservableObject
{
    public PhysicalPin Pin { get; }
    public ComponentViewModel ParentComponentViewModel { get; }

    [ObservableProperty]
    private bool _isHighlighted;

    /// <summary>
    /// Scale factor for visual size (1.0 = normal, 1.5 = highlighted).
    /// </summary>
    [ObservableProperty]
    private double _scale = 1.0;

    public double X => Pin.GetAbsolutePosition().x;
    public double Y => Pin.GetAbsolutePosition().y;
    public double Angle => Pin.GetAbsoluteAngle();
    public string Name => Pin.Name;

    /// <summary>
    /// Whether this pin already has a connection.
    /// </summary>
    public bool HasConnection { get; set; }

    public PinViewModel(PhysicalPin pin, ComponentViewModel parentVm)
    {
        Pin = pin;
        ParentComponentViewModel = parentVm;
    }

    public void SetHighlighted(bool highlighted)
    {
        IsHighlighted = highlighted;
        Scale = highlighted ? 1.5 : 1.0;
    }

    public void NotifyPositionChanged()
    {
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(Angle));
    }
}
