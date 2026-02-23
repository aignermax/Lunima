using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CAP_Core.Components;
using CAP_Core.Routing;
using CAP.Avalonia.Visualization;

namespace CAP.Avalonia.ViewModels;

public partial class DesignCanvasViewModel : ObservableObject
{
    public ObservableCollection<ComponentViewModel> Components { get; } = new();
    public ObservableCollection<WaveguideConnectionViewModel> Connections { get; } = new();
    public ObservableCollection<PinViewModel> AllPins { get; } = new();

    public WaveguideConnectionManager ConnectionManager { get; } = new();

    /// <summary>
    /// Manages power flow visualization state and data.
    /// </summary>
    public PowerFlowVisualizer PowerFlowVisualizer { get; } = new();

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
        // Recalculate all waveguide routes with new bend radius
        ConnectionManager.RecalculateAllTransmissions();
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
        Router.Strategy = UseAStarRouting ? RoutingStrategy.Auto : RoutingStrategy.Manhattan;

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
    }

    /// <summary>
    /// Reinitializes the A* pathfinding grid with custom bounds.
    /// </summary>
    public void InitializeAStarRouting(double minX, double minY, double maxX, double maxY)
    {
        Router.Strategy = UseAStarRouting ? RoutingStrategy.Auto : RoutingStrategy.Manhattan;
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
        Router.Strategy = value ? RoutingStrategy.Auto : RoutingStrategy.Manhattan;
        // Recalculate all connections with new routing strategy
        ConnectionManager.RecalculateAllTransmissions();
        foreach (var conn in Connections)
        {
            conn.NotifyPathChanged();
        }
    }

    /// <summary>
    /// Call when starting to drag a component. Disables expensive recalculations.
    /// </summary>
    public void BeginDragComponent(ComponentViewModel component)
    {
        _isDragging = true;
    }

    /// <summary>
    /// Call when done dragging a component. Triggers final route recalculation.
    /// </summary>
    public void EndDragComponent(ComponentViewModel component)
    {
        _isDragging = false;

        // Rebuild the entire obstacle grid to ensure all components are correctly marked
        // This is necessary because the moved component may now block paths for other connections
        if (Router.PathfindingGrid != null)
        {
            Router.PathfindingGrid.RebuildFromComponents(Components.Select(c => c.Component));
        }

        // Recalculate all routes since any connection could now be affected
        ConnectionManager.RecalculateAllTransmissions();

        // Notify UI about all connections (paths may have changed globally)
        foreach (var conn in Connections)
        {
            conn.NotifyPathChanged();
        }

        InvalidateSimulation();
    }

    /// <summary>
    /// Checks if a component can be placed at the given position without overlapping others.
    /// </summary>
    public bool CanMoveComponentTo(ComponentViewModel component, double x, double y)
    {
        return CanPlaceComponent(x, y, component.Width, component.Height, excludeComponent: component);
    }

    public void MoveComponent(ComponentViewModel component, double deltaX, double deltaY)
    {
        double newX = component.X + deltaX;
        double newY = component.Y + deltaY;

        // During drag: allow free movement (collision checked on drop)
        // Otherwise: check for overlap
        if (!_isDragging && !CanPlaceComponent(newX, newY, component.Width, component.Height, excludeComponent: component))
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
            // Not dragging (e.g., programmatic move): recalculate immediately
            Router.UpdateComponentObstacle(component.Component);
            ConnectionManager.RecalculateTransmissionsForComponent(component.Component);

            foreach (var conn in Connections)
            {
                if (conn.Connection.StartPin.ParentComponent == component.Component ||
                    conn.Connection.EndPin.ParentComponent == component.Component)
                {
                    conn.NotifyPathChanged();
                }
            }
        }
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
        InvalidateSimulation();
    }

    public WaveguideConnectionViewModel? ConnectPins(PhysicalPin startPin, PhysicalPin endPin)
    {
        // Remove any existing connections on either pin (only one waveguide per pin allowed)
        RemoveConnectionsForPin(startPin);
        RemoveConnectionsForPin(endPin);

        var connection = ConnectionManager.AddConnection(startPin, endPin);
        var vm = new WaveguideConnectionViewModel(connection);
        Connections.Add(vm);

        // Notify all connections that paths may have changed (due to sequential re-routing)
        foreach (var conn in Connections)
        {
            conn.NotifyPathChanged();
        }

        InvalidateSimulation();
        return vm;
    }

    /// <summary>
    /// Removes all connections that involve a specific pin.
    /// </summary>
    public void RemoveConnectionsForPin(PhysicalPin pin)
    {
        var connectionsToRemove = Connections
            .Where(c => c.Connection.StartPin == pin || c.Connection.EndPin == pin)
            .ToList();

        foreach (var conn in connectionsToRemove)
        {
            ConnectionManager.RemoveConnection(conn.Connection);
            Connections.Remove(conn);
        }

        // Notify remaining connections that paths may have changed
        foreach (var conn in Connections)
        {
            conn.NotifyPathChanged();
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
