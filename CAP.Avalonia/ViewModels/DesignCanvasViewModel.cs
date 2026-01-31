using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CAP_Core.Components;
using CAP_Core.Routing;

namespace CAP.Avalonia.ViewModels;

public partial class DesignCanvasViewModel : ObservableObject
{
    public ObservableCollection<ComponentViewModel> Components { get; } = new();
    public ObservableCollection<WaveguideConnectionViewModel> Connections { get; } = new();

    public WaveguideConnectionManager ConnectionManager { get; } = new();

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
    }

    public void MoveComponent(ComponentViewModel component, double deltaX, double deltaY)
    {
        double newX = component.X + deltaX;
        double newY = component.Y + deltaY;

        // Check for overlap with other components
        if (!CanPlaceComponent(newX, newY, component.Width, component.Height, excludeComponent: component))
        {
            // Position would overlap - don't move
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
        Components.Add(vm);

        // Add obstacle to A* pathfinding grid
        Router.AddComponentObstacle(component);

        return vm;
    }

    public void RemoveComponent(ComponentViewModel component)
    {
        // Remove obstacle from A* pathfinding grid
        Router.RemoveComponentObstacle(component.Component);

        ConnectionManager.RemoveConnectionsForComponent(component.Component);

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

    public double Width => Component.WidthMicrometers;
    public double Height => Component.HeightMicrometers;
    public string Name => Component.Identifier;

    public ComponentViewModel(Component component, string? templateName = null)
    {
        Component = component;
        TemplateName = templateName;
        _x = component.PhysicalX;
        _y = component.PhysicalY;
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
