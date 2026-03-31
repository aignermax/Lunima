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
using CAP.Avalonia.ViewModels.Canvas.Services;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>
/// Thin orchestrator for the design canvas. Delegates to specialized services
/// for routing, placement, group editing, simulation, and pin highlighting.
/// </summary>
public partial class DesignCanvasViewModel : ObservableObject
{
    // ── Collections (bound by AXAML) ──────────────────────────────────────
    public ObservableCollection<ComponentViewModel> Components { get; } = new();
    public ObservableCollection<WaveguideConnectionViewModel> Connections { get; } = new();
    public ObservableCollection<PinViewModel> AllPins { get; } = new();

    // ── Core dependencies ─────────────────────────────────────────────────
    public WaveguideConnectionManager ConnectionManager { get; }
    public WaveguideRouter Router { get; }

    // ── Extracted services ────────────────────────────────────────────────
    public GroupEditService Groups { get; }
    public RoutingOrchestrator Routing { get; }
    public ComponentPlacementService Placement { get; }
    public SimulationCoordinator Simulation { get; }
    public PinHighlightService PinHighlight { get; }

    // ── UI services (pre-existing) ────────────────────────────────────────
    public SelectionManager Selection { get; } = new();
    public ComponentClipboard Clipboard { get; } = new();
    public PowerFlowVisualizer PowerFlowVisualizer { get; } = new();
    public AlignmentGuideViewModel AlignmentGuide { get; } = new();
    public GridSnapSettings GridSnap { get; } = new();

    // ── Observable properties (for AXAML bindings) ────────────────────────
    [ObservableProperty] private bool _showPowerFlow;
    [ObservableProperty] private bool _useAStarRouting = true;
    [ObservableProperty] private bool _showGridOverlay;
    [ObservableProperty] private double _minBendRadiusMicrometers = 10.0;
    [ObservableProperty] private ComponentViewModel? _selectedComponent;
    [ObservableProperty] private double _panX;
    [ObservableProperty] private double _panY;
    [ObservableProperty] private bool _isRouting;
    [ObservableProperty] private string _routingStatusText = "";
    [ObservableProperty] private ComponentGroup? _currentEditGroup;

    // ── Callbacks ─────────────────────────────────────────────────────────
    public Action? SimulationRequested
    {
        get => Simulation.SimulationRequested;
        set => Simulation.SimulationRequested = value;
    }

    public Action? RepaintRequested
    {
        get => Routing.RepaintRequested;
        set => Routing.RepaintRequested = value;
    }

    // ── Delegated read-only properties ────────────────────────────────────
    public bool IsInGroupEditMode => Groups.IsInGroupEditMode;
    public ObservableCollection<ComponentGroup> BreadcrumbPath => Groups.BreadcrumbPath;
    public double PinHighlightDistance
    {
        get => PinHighlight.PinHighlightDistance;
        set => PinHighlight.PinHighlightDistance = value;
    }
    public PinViewModel? HighlightedPin => PinHighlight.HighlightedPin;
    public double ChipMinX { get => Placement.ChipMinX; set => Placement.ChipMinX = value; }
    public double ChipMinY { get => Placement.ChipMinY; set => Placement.ChipMinY = value; }
    public double ChipMaxX { get => Placement.ChipMaxX; set => Placement.ChipMaxX = value; }
    public double ChipMaxY { get => Placement.ChipMaxY; set => Placement.ChipMaxY = value; }

    // ── Constructors ──────────────────────────────────────────────────────

    /// <summary>
    /// Initializes a new instance with a fresh <see cref="WaveguideRouter"/>.
    /// </summary>
    public DesignCanvasViewModel() : this(new WaveguideRouter()) { }

    /// <summary>
    /// Initializes a new instance with an injected router.
    /// </summary>
    public DesignCanvasViewModel(WaveguideRouter router)
    {
        Router = router;
        ConnectionManager = new WaveguideConnectionManager(router);

        Routing = new RoutingOrchestrator(router, ConnectionManager, Components, Connections);
        Placement = new ComponentPlacementService(Components, Connections);
        Simulation = new SimulationCoordinator(PowerFlowVisualizer);
        PinHighlight = new PinHighlightService(AllPins, GetConnectionForPin);
        Groups = new GroupEditService(
            Components, Connections, AllPins, ConnectionManager, router,
            (comp, tpl, pdk) => AddComponent(comp, tpl, pdk),
            BeginCommandExecution, EndCommandExecution,
            () => Routing.InitializeAStarRouting(),
            () => Routing.RecalculateRoutesAsync());

        // Wire service events to observable property updates
        Routing.StateChanged += () =>
        {
            IsRouting = Routing.IsRouting;
            RoutingStatusText = Routing.RoutingStatusText;
        };
        // Pre-change: update VM property BEFORE collections are modified
        // (HierarchyPanelViewModel.RebuildTree reads _canvas.CurrentEditGroup on CollectionChanged)
        Groups.CurrentEditGroupChanging += group =>
        {
            CurrentEditGroup = group;
            OnPropertyChanged(nameof(IsInGroupEditMode));
        };
        Groups.EditStateChanged += () =>
        {
            CurrentEditGroup = Groups.CurrentEditGroup;
            OnPropertyChanged(nameof(IsInGroupEditMode));
        };
        PinHighlight.HighlightChanged += () => OnPropertyChanged(nameof(HighlightedPin));
        Simulation.ShowPowerFlowChanged += (value, forceNotify) =>
        {
            if (forceNotify && ShowPowerFlow == value)
                OnPropertyChanged(nameof(ShowPowerFlow));
            else
                ShowPowerFlow = value;
        };

        Routing.InitializeAStarRouting();
    }

    // ── Property change handlers ──────────────────────────────────────────

    partial void OnMinBendRadiusMicrometersChanged(double value)
    {
        Router.MinBendRadiusMicrometers = value;
        _ = RecalculateRoutesAsync();
    }

    partial void OnUseAStarRoutingChanged(bool value) => _ = RecalculateRoutesAsync();

    // ── Simulation delegation ─────────────────────────────────────────────

    public void InvalidateSimulation() => Simulation.InvalidateSimulation(ShowPowerFlow);
    public void RequestResimulation() => Simulation.RequestResimulation(ShowPowerFlow);
    public void RefreshPowerFlowDisplay() => Simulation.RefreshPowerFlowDisplay(ShowPowerFlow);

    // ── Routing delegation ────────────────────────────────────────────────

    public void InitializeAStarRouting() => Routing.InitializeAStarRouting();
    public void InitializeAStarRouting(double minX, double minY, double maxX, double maxY)
        => Routing.InitializeAStarRouting(minX, minY, maxX, maxY);
    public Task RecalculateRoutesAsync() => Routing.RecalculateRoutesAsync();

    // ── Drag / command execution ──────────────────────────────────────────

    public void BeginDragComponent(ComponentViewModel component) => Placement.IsDragging = true;
    public void BeginCommandExecution() => Placement.IsExecutingCommand = true;
    public void EndCommandExecution() => Placement.IsExecutingCommand = false;

    public async void EndDragComponent(ComponentViewModel component)
    {
        Placement.IsDragging = false;
        await RecalculateRoutesAsync();
        InvalidateSimulation();
    }

    // ── Movement / placement delegation ───────────────────────────────────

    public bool CanMoveComponentTo(ComponentViewModel component, double x, double y)
        => Placement.CanMoveComponentTo(component, x, y);

    public void MoveComponent(ComponentViewModel component, double deltaX, double deltaY)
        => Placement.MoveComponent(component, deltaX, deltaY,
            Groups.IsInGroupEditMode, Groups.CurrentEditGroup,
            Groups.UpdateExternalPinPositions, Routing.RecalculateRoutesAsync);

    public bool CanPlaceComponent(double x, double y, double width, double height,
        ComponentViewModel? excludeComponent = null)
        => Placement.CanPlaceComponent(x, y, width, height, excludeComponent);

    public (double x, double y)? FindValidPlacement(double x, double y, double width, double height)
        => Placement.FindValidPlacement(x, y, width, height);

    // ── Group editing delegation ──────────────────────────────────────────

    public void EnterGroupEditMode(ComponentGroup group) => Groups.EnterGroupEditMode(group);
    public void ExitGroupEditMode() => Groups.ExitGroupEditMode();
    [RelayCommand] public void ExitToRoot() => Groups.ExitToRoot();
    [RelayCommand] public void NavigateToBreadcrumbLevel(ComponentGroup? group)
        => Groups.NavigateToBreadcrumbLevel(group);

    // ── Pin highlight delegation ──────────────────────────────────────────

    public PinViewModel? UpdatePinHighlight(double x, double y, PhysicalPin? excludePin = null)
        => PinHighlight.UpdatePinHighlight(x, y, excludePin);
    public void ClearPinHighlight() => PinHighlight.ClearPinHighlight();
    public PhysicalPin? GetPinAt(double x, double y, double tolerance = 15.0)
        => PinHighlight.GetPinAt(x, y, tolerance);

    // ── Component lifecycle ───────────────────────────────────────────────

    public ComponentViewModel AddComponent(Component component,
        string? templateName = null, string? templatePdkSource = null)
    {
        var vm = new ComponentViewModel(component, templateName, templatePdkSource);
        vm.OnSliderChanged = () => RequestResimulation();
        Components.Add(vm);
        Router.AddComponentObstacle(component);

        foreach (var pin in component.PhysicalPins)
            AllPins.Add(new PinViewModel(pin, vm));

        if (component is ComponentGroup group)
        {
            foreach (var groupPin in group.ExternalPins)
                AllPins.Add(new PinViewModel(groupPin.InternalPin, vm));
        }

        return vm;
    }

    public void RemoveComponent(ComponentViewModel component)
    {
        Router.RemoveComponentObstacle(component.Component);
        ConnectionManager.RemoveConnectionsForComponent(component.Component);

        var pinsToRemove = AllPins.Where(p => p.ParentComponentViewModel == component).ToList();
        foreach (var pin in pinsToRemove) AllPins.Remove(pin);

        var connectionsToRemove = Connections
            .Where(c => c.Connection.StartPin.ParentComponent == component.Component ||
                        c.Connection.EndPin.ParentComponent == component.Component).ToList();
        foreach (var conn in connectionsToRemove) Connections.Remove(conn);

        Components.Remove(component);
        if (ConnectionManager.Connections.Count > 0) _ = RecalculateRoutesAsync();
        InvalidateSimulation();
    }

    // ── Connection management ─────────────────────────────────────────────

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

    public WaveguideConnectionViewModel? ConnectPins(PhysicalPin startPin, PhysicalPin endPin)
    {
        RemoveConnectionsForPin(startPin);
        RemoveConnectionsForPin(endPin);
        var connection = ConnectionManager.AddConnectionDeferred(startPin, endPin);
        var vm = new WaveguideConnectionViewModel(connection);
        Connections.Add(vm);
        InvalidateSimulation();
        return vm;
    }

    public WaveguideConnectionViewModel? ConnectPinsWithCachedRoute(
        PhysicalPin startPin, PhysicalPin endPin, RoutedPath cachedRoute)
    {
        RemoveConnectionsForPin(startPin);
        RemoveConnectionsForPin(endPin);
        var connection = ConnectionManager.AddConnectionWithCachedRoute(startPin, endPin, cachedRoute);
        var vm = new WaveguideConnectionViewModel(connection);
        Connections.Add(vm);
        return vm;
    }

    public void RemoveConnectionsForPin(PhysicalPin pin)
    {
        var connectionsToRemove = Connections
            .Where(c => c.Connection.StartPin == pin || c.Connection.EndPin == pin).ToList();
        foreach (var conn in connectionsToRemove)
        {
            ConnectionManager.RemoveConnectionDeferred(conn.Connection);
            Connections.Remove(conn);
        }
        if (connectionsToRemove.Count > 0) InvalidateSimulation();
    }

    public WaveguideConnectionViewModel? GetConnectionForPin(PhysicalPin pin)
        => Connections.FirstOrDefault(c =>
            c.Connection.StartPin == pin || c.Connection.EndPin == pin);
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
    /// Display name for UI - uses HumanReadableName if available, falls back to Name.
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (Component is ComponentGroup group) return group.GroupName;
            return Component.HumanReadableName ?? Component.Name;
        }
    }

    /// <summary>
    /// The name of the template used to create this component.
    /// </summary>
    public string? TemplateName { get; set; }

    /// <summary>
    /// The PDK source of the template (e.g. "Built-in", "Demo PDK").
    /// </summary>
    public string? TemplatePdkSource { get; set; }

    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private bool _isSelected;

    /// <summary>
    /// Whether this component is locked (cannot be moved, rotated, or deleted).
    /// </summary>
    public bool IsLocked => Component.IsLocked;

    /// <summary>
    /// Notifies that the lock state has changed.
    /// </summary>
    public void NotifyLockStateChanged() => OnPropertyChanged(nameof(IsLocked));

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
    public string Name => Component.HumanReadableName ?? Component.Identifier;

    /// <summary>
    /// Whether this ComponentViewModel represents a ComponentGroup.
    /// </summary>
    public bool IsComponentGroup => Component is ComponentGroup;

    /// <summary>
    /// Whether this component has adjustable slider parameters.
    /// </summary>
    public bool HasSliders => Component.GetAllSliders().Count > 0;

    /// <summary>
    /// Display label for the slider.
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
    /// Callback to notify the canvas that a slider changed.
    /// </summary>
    public Action? OnSliderChanged { get; set; }

    /// <summary>
    /// First slider's current value.
    /// </summary>
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

    public ComponentViewModel(Component component, string? templateName = null, string? templatePdkSource = null)
    {
        Component = component;
        TemplateName = templateName;
        TemplatePdkSource = templatePdkSource;
        _x = component.PhysicalX;
        _y = component.PhysicalY;

        if (templateName != null && LightSourceNames.Contains(templateName))
            LaserConfig = new LaserConfig();
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

    [ObservableProperty] private bool _isSelected;

    public double StartX => Connection.StartPin.GetAbsolutePosition().x;
    public double StartY => Connection.StartPin.GetAbsolutePosition().y;
    public double EndX => Connection.EndPin.GetAbsolutePosition().x;
    public double EndY => Connection.EndPin.GetAbsolutePosition().y;
    public double PathLength => Connection.PathLengthMicrometers;
    public double LossDb => Connection.TotalLossDb;
    public bool IsBlockedFallback => Connection.IsBlockedFallback;
    public bool IsTargetLengthEnabled => Connection.IsTargetLengthEnabled;
    public double? TargetLengthMicrometers => Connection.TargetLengthMicrometers;
    public bool? IsLengthMatched => Connection.IsLengthMatched;
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

    [ObservableProperty] private bool _isHighlighted;
    [ObservableProperty] private double _scale = 1.0;

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
