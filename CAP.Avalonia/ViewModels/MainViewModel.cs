using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Converters;
using CAP.Avalonia.ViewModels.Panels;

namespace CAP.Avalonia.ViewModels;

public enum InteractionMode
{
    Select,
    PlaceComponent,
    Connect,
    Delete
}

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private DesignCanvasViewModel _canvas;

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    [ObservableProperty]
    private InteractionMode _currentMode = InteractionMode.Select;

    /// <summary>
    /// Left panel ViewModel (component library, search, PDK management).
    /// </summary>
    public LeftPanelViewModel LeftPanel { get; } = new();

    /// <summary>
    /// Right panel ViewModel (properties, sweep, diagnostics).
    /// </summary>
    public RightPanelViewModel RightPanel { get; } = new();

    /// <summary>
    /// Bottom panel ViewModel (status text).
    /// </summary>
    public BottomPanelViewModel BottomPanel { get; } = new();

    public Commands.CommandManager CommandManager { get; }
    public SimulationService Simulation { get; }
    public IFileDialogService? FileDialogService { get; set; }

    private readonly SimpleNazcaExporter _nazcaExporter;
    private readonly PdkLoader _pdkLoader;
    private readonly UserPreferencesService _preferencesService;
    private PhysicalPin? _connectionStartPin;
    private string? _currentFilePath;
    private bool _isSimulating;

    // For tracking move operations
    private double _moveStartX;
    private double _moveStartY;
    private ComponentViewModel? _movingComponent;

    // For tracking group move operations
    private Dictionary<ComponentViewModel, (double x, double y)>? _groupMoveStartPositions;

    /// <summary>
    /// Callback to get the current canvas viewport size (width, height) in screen pixels.
    /// Set by the View code-behind (MainWindow) after initialization.
    /// </summary>
    public Func<(double width, double height)>? GetViewportSize { get; set; }

    public MainViewModel(
        SimulationService simulationService,
        SimpleNazcaExporter nazcaExporter,
        PdkLoader pdkLoader,
        Commands.CommandManager commandManager,
        UserPreferencesService preferencesService)
    {
        Simulation = simulationService;
        _nazcaExporter = nazcaExporter;
        _pdkLoader = pdkLoader;
        CommandManager = commandManager;
        _preferencesService = preferencesService;
        _canvas = new DesignCanvasViewModel();
        _canvas.SimulationRequested = async () => await ExecuteSimulation();

        // Wire up panel ViewModels to canvas
        RightPanel.RoutingDiagnostics.Configure(_canvas);
        RightPanel.DimensionDiagnostics = new Diagnostics.ComponentDimensionDiagnosticsViewModel(_canvas);
        LeftPanel.ElementLock.Configure(_canvas, CommandManager);
        RightPanel.DimensionValidator.Configure(_canvas);
        RightPanel.CompressLayout.Configure(_canvas, CommandManager);
        LeftPanel.HierarchyPanel.Configure(_canvas);

        _canvas.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(DesignCanvasViewModel.RoutingStatusText))
            {
                var routingText = _canvas.RoutingStatusText;
                if (!string.IsNullOrEmpty(routingText))
                    BottomPanel.StatusText = routingText;
            }
        };

        // Listen to selection changes to update command availability
        _canvas.Selection.SelectedComponents.CollectionChanged += (s, e) =>
        {
            CreateGroupCommand.NotifyCanExecuteChanged();
        };

        WireDesignValidation();
        WirePanelCallbacks();
        LoadComponentLibrary();
        RestorePdkFilterState();
    }

    private void WirePanelCallbacks()
    {
        // Wire up PDK filter changed callback
        LeftPanel.PdkManager.OnFilterChanged = FilterComponents;
        LeftPanel.OnFilterChanged = FilterComponents;

        // Wire up ComponentGroup callbacks
        LeftPanel.ComponentGroups.OnCreateGroupFromSelection = CreateGroupFromSelection;
        LeftPanel.ComponentGroups.OnPlaceGroup = PlaceComponentGroup;

        // Wire up HierarchyPanel focus callback
        LeftPanel.HierarchyPanel.OnFocusRequested = NavigateCanvasTo;

        // Wire up selected component/connection sync between panels
        LeftPanel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(LeftPanel.SelectedTemplate) && LeftPanel.SelectedTemplate != null)
            {
                CurrentMode = InteractionMode.PlaceComponent;
                BottomPanel.StatusText = $"Click on canvas to place: {LeftPanel.SelectedTemplate.Name}";
            }
        };

        // Sync Sweep configuration with Canvas
        RightPanel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(RightPanel.SelectedComponent))
            {
                RightPanel.Sweep.ConfigureForComponent(RightPanel.SelectedComponent, Canvas);
                UpdateStatusForSelectedComponent();
            }
        };
    }

    private void UpdateStatusForSelectedComponent()
    {
        var comp = RightPanel.SelectedComponent;
        if (comp?.IsLightSource == true)
        {
            var cfg = comp.LaserConfig!;
            BottomPanel.StatusText = $"Selected: {comp.Name} [{cfg.WavelengthLabel}, Power={cfg.InputPower:F2}]";
        }
    }

    private void LoadComponentLibrary()
    {
        var templates = ComponentTemplates.GetAllTemplates();

        foreach (var template in templates)
        {
            LeftPanel.ComponentLibrary.Add(template);
        }

        // Register built-in templates as a PDK
        if (templates.Count > 0)
        {
            LeftPanel.PdkManager.RegisterPdk("Built-in Components", null, true, templates.Count);
        }

        // Auto-load bundled PDK files from PDKs directory
        LoadBundledPdks();

        // Build category list from all loaded templates
        var categories = LeftPanel.ComponentLibrary.Select(t => t.Category).Distinct().OrderBy(c => c);
        foreach (var category in categories)
        {
            LeftPanel.Categories.Add(category);
        }

        BottomPanel.StatusText = $"Loaded {LeftPanel.ComponentLibrary.Count} component types";
        FilterComponents();
    }

    private void LoadBundledPdks()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var pdkDir = Path.Combine(baseDir, "PDKs");

        if (!Directory.Exists(pdkDir))
            return;

        foreach (var pdkFile in Directory.GetFiles(pdkDir, "*.json"))
        {
            try
            {
                var pdk = _pdkLoader.LoadFromFile(pdkFile);
                int componentCount = 0;
                foreach (var pdkComp in pdk.Components)
                {
                    var template = ConvertPdkComponentToTemplate(pdkComp, pdk.Name, pdk.NazcaModuleName);
                    LeftPanel.ComponentLibrary.Add(template);
                    componentCount++;
                }

                // Register bundled PDK with the manager
                LeftPanel.PdkManager.RegisterPdk(pdk.Name, pdkFile, true, componentCount);
            }
            catch
            {
                // Skip malformed PDK files silently at startup
            }
        }
    }

    private void FilterComponents()
    {
        var enabledPdks = LeftPanel.PdkManager.GetEnabledPdkNames();
        LeftPanel.FilterComponents(enabledPdks);
        SavePdkFilterState();
    }

    private void RestorePdkFilterState()
    {
        var enabledPdks = _preferencesService.GetEnabledPdks();

        // If no preferences saved, enable all by default
        if (enabledPdks.Count == 0)
            return;

        // Apply saved filter state
        foreach (var pdk in LeftPanel.PdkManager.LoadedPdks)
        {
            pdk.IsEnabled = enabledPdks.Contains(pdk.Name);
        }

        FilterComponents();
    }

    private void SavePdkFilterState()
    {
        var enabledPdks = LeftPanel.PdkManager.GetEnabledPdkNames();
        _preferencesService.SetEnabledPdks(enabledPdks);
    }

    partial void OnCurrentModeChanged(InteractionMode value)
    {
        _connectionStartPin = null; // Reset connection state when mode changes
        Canvas.ClearPinHighlight(); // Clear pin highlighting when mode changes

        BottomPanel.StatusText = value switch
        {
            InteractionMode.Select => "Select mode: Click to select, drag to move",
            InteractionMode.PlaceComponent when LeftPanel.SelectedTemplate != null => $"Place mode: Click to place {LeftPanel.SelectedTemplate.Name}",
            InteractionMode.PlaceComponent => "Place mode: Select a component from the library",
            InteractionMode.Connect => "Connect mode: Move near a pin to start connection",
            InteractionMode.Delete => "Delete mode: Click on component or connection to delete",
            _ => "Ready"
        };
    }

    public void CanvasClicked(double canvasX, double canvasY)
    {
        switch (CurrentMode)
        {
            case InteractionMode.PlaceComponent:
                PlaceComponentAt(canvasX, canvasY);
                break;
            case InteractionMode.Select:
                SelectAt(canvasX, canvasY);
                break;
            case InteractionMode.Connect:
                var pin = Canvas.HighlightedPin?.Pin ?? Canvas.GetPinAt(canvasX, canvasY);
                if (pin != null)
                {
                    HandlePinClickForConnection(pin);
                }
                break;
            case InteractionMode.Delete:
                DeleteAt(canvasX, canvasY);
                break;
        }
    }

    public void PinClicked(PhysicalPin pin)
    {
        if (CurrentMode == InteractionMode.Connect)
        {
            HandlePinClickForConnection(pin);
        }
    }

    /// <summary>
    /// Called when mouse moves on the canvas. Used for pin highlighting in Connect mode.
    /// </summary>
    public void CanvasMouseMove(double canvasX, double canvasY)
    {
        if (CurrentMode == InteractionMode.Connect)
        {
            var nearPin = Canvas.UpdatePinHighlight(canvasX, canvasY, _connectionStartPin);

            if (nearPin != null)
            {
                var pinName = nearPin.Name;
                var compName = nearPin.ParentComponentViewModel.Name;

                if (_connectionStartPin != null)
                {
                    BottomPanel.StatusText = $"Click to connect to {pinName} on {compName}";
                }
                else
                {
                    BottomPanel.StatusText = $"Click {pinName} on {compName} to start connection";
                }
            }
            else if (_connectionStartPin != null)
            {
                BottomPanel.StatusText = $"Connection started from {_connectionStartPin.Name}. Move near a pin to connect.";
            }
            else
            {
                BottomPanel.StatusText = "Connect mode: Move near a pin to start connection";
            }
        }
        else
        {
            Canvas.ClearPinHighlight();
        }
    }

    private void HandlePinClickForConnection(PhysicalPin pin)
    {
        if (_connectionStartPin == null)
        {
            _connectionStartPin = pin;
            BottomPanel.StatusText = $"Connection started from {pin.Name}. Click another pin to complete.";
        }
        else
        {
            if (_connectionStartPin != pin && _connectionStartPin.ParentComponent != pin.ParentComponent)
            {
                var cmd = new CreateConnectionCommand(Canvas, _connectionStartPin, pin);
                CommandManager.ExecuteCommand(cmd);
                BottomPanel.StatusText = $"Connected {_connectionStartPin.Name} to {pin.Name}";
            }
            else
            {
                BottomPanel.StatusText = "Cannot connect pin to itself or same component";
            }
            _connectionStartPin = null;
        }
    }

    private void PlaceComponentAt(double x, double y)
    {
        if (LeftPanel.SelectedTemplate == null) return;

        // Center the component at the click position
        double centeredX = x - LeftPanel.SelectedTemplate.WidthMicrometers / 2;
        double centeredY = y - LeftPanel.SelectedTemplate.HeightMicrometers / 2;

        var cmd = PlaceComponentCommand.TryCreate(Canvas, LeftPanel.SelectedTemplate, centeredX, centeredY);
        if (cmd == null)
        {
            BottomPanel.StatusText = "No space available on chip for this component";
            return;
        }

        CommandManager.ExecuteCommand(cmd);
        BottomPanel.StatusText = $"Placed {LeftPanel.SelectedTemplate.Name} at ({x:F0}, {y:F0})µm";
    }

    private void CreateGroupFromSelection(string name, string category, string description)
    {
        var selectedComponents = Canvas.Components.Where(c => c.IsSelected).ToList();
        if (selectedComponents.Count == 0)
        {
            BottomPanel.StatusText = "No components selected - select components first";
            return;
        }

        // Get the actual Component objects
        var components = selectedComponents.Select(vm => vm.Component).ToList();

        // Get connections between selected components
        var componentSet = new HashSet<Component>(components);
        var connections = Canvas.Connections
            .Where(connVm =>
                componentSet.Contains(connVm.Connection.StartPin.ParentComponent) &&
                componentSet.Contains(connVm.Connection.EndPin.ParentComponent))
            .Select(connVm => connVm.Connection)
            .ToList();

        // Create the group
        var groupManager = new CAP_Core.Components.ComponentHelpers.ComponentGroupManager(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ConnectAPicPro",
                "component-groups.json"));

        var group = groupManager.CreateGroupFromComponents(name, category, components, connections);
        group.Description = description;

        // Save via ViewModel (which will refresh the UI)
        LeftPanel.ComponentGroups.SaveGroup(group);

        BottomPanel.StatusText = $"Created group '{name}' with {components.Count} components";
    }

    private void PlaceComponentGroup(CAP_Core.Components.ComponentHelpers.ComponentGroup group)
    {
        // For now, place at a fixed offset - in a full implementation,
        // this would enter a placement mode similar to PlaceComponent
        double offsetX = 200;
        double offsetY = 200;

        var placedComponents = new List<ComponentViewModel>();
        var componentMap = new Dictionary<int, Component>();

        // Create and place all components in the group
        foreach (var member in group.Components)
        {
            var template = LeftPanel.ComponentLibrary.FirstOrDefault(t => t.Name == member.TemplateName);
            if (template == null)
            {
                BottomPanel.StatusText = $"Template '{member.TemplateName}' not found in library";
                continue;
            }

            var component = ComponentTemplates.CreateFromTemplate(
                template,
                offsetX + member.RelativeX,
                offsetY + member.RelativeY);

            component.Rotation90CounterClock = member.Rotation;

            // Apply parameter values
            var sliders = component.GetAllSliders();
            for (int i = 0; i < sliders.Count && i < member.Parameters.Count; i++)
            {
                if (member.Parameters.TryGetValue($"Slider{i}", out var value))
                {
                    sliders[i].Value = value;
                }
            }

            var cmd = PlaceComponentCommand.TryCreate(Canvas, template, component.PhysicalX, component.PhysicalY);
            if (cmd != null)
            {
                CommandManager.ExecuteCommand(cmd);
                var vm = Canvas.Components.LastOrDefault();
                if (vm != null)
                {
                    placedComponents.Add(vm);
                    componentMap[member.LocalId] = vm.Component;
                }
            }
        }

        // Create connections between components
        foreach (var connDef in group.Connections)
        {
            if (!componentMap.TryGetValue(connDef.SourceComponentId, out var sourceComp) ||
                !componentMap.TryGetValue(connDef.TargetComponentId, out var targetComp))
                continue;

            var sourcePin = sourceComp.PhysicalPins.FirstOrDefault(p => p.Name == connDef.SourcePinName);
            var targetPin = targetComp.PhysicalPins.FirstOrDefault(p => p.Name == connDef.TargetPinName);

            if (sourcePin != null && targetPin != null)
            {
                var connCmd = new CreateConnectionCommand(Canvas, sourcePin, targetPin);
                CommandManager.ExecuteCommand(connCmd);
            }
        }

        BottomPanel.StatusText = $"Placed group '{group.Name}' with {placedComponents.Count} components";
    }

    private void SelectAt(double x, double y)
    {
        // Deselect all
        foreach (var comp in Canvas.Components)
        {
            comp.IsSelected = false;
        }
        foreach (var conn in Canvas.Connections)
        {
            conn.IsSelected = false;
        }

        // Find component at position
        var component = Canvas.Components
            .Where(c => x >= c.X && x <= c.X + c.Width && y >= c.Y && y <= c.Y + c.Height)
            .LastOrDefault();

        if (component != null)
        {
            component.IsSelected = true;
            RightPanel.SelectedComponent = component;
            Canvas.SelectedComponent = component;
            RightPanel.SelectedWaveguideConnection = null;
            BottomPanel.StatusText = $"Selected: {component.Name}";
        }
        else
        {
            // Check for connection at position
            var connection = FindConnectionAt(x, y);
            if (connection != null)
            {
                connection.IsSelected = true;
                RightPanel.SelectedWaveguideConnection = connection;
                RightPanel.SelectedComponent = null;
                Canvas.SelectedComponent = null;
                BottomPanel.StatusText = $"Selected connection: {connection.PathLength:F1}µm, Loss: {connection.LossDb:F2}dB";
            }
            else
            {
                RightPanel.SelectedComponent = null;
                Canvas.SelectedComponent = null;
                RightPanel.SelectedWaveguideConnection = null;
            }
        }
    }

    private void DeleteAt(double x, double y)
    {
        var component = Canvas.Components
            .Where(c => x >= c.X && x <= c.X + c.Width && y >= c.Y && y <= c.Y + c.Height)
            .LastOrDefault();

        if (component != null)
        {
            var name = component.Name;
            var cmd = new DeleteComponentCommand(Canvas, component);
            CommandManager.ExecuteCommand(cmd);
            RightPanel.SelectedComponent = null;
            BottomPanel.StatusText = $"Deleted: {name}";
            return;
        }

        var connection = FindConnectionAt(x, y);
        if (connection != null)
        {
            var cmd = new DeleteConnectionCommand(Canvas, connection);
            CommandManager.ExecuteCommand(cmd);
            BottomPanel.StatusText = "Deleted connection";
        }
    }

    private WaveguideConnectionViewModel? FindConnectionAt(double x, double y)
    {
        const double hitTolerance = 10.0;

        foreach (var conn in Canvas.Connections)
        {
            var distance = PointToLineDistance(x, y, conn.StartX, conn.StartY, conn.EndX, conn.EndY);
            if (distance <= hitTolerance)
            {
                return conn;
            }
        }
        return null;
    }

    private static double PointToLineDistance(double px, double py, double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var lengthSq = dx * dx + dy * dy;

        if (lengthSq < 0.0001)
        {
            return Math.Sqrt((px - x1) * (px - x1) + (py - y1) * (py - y1));
        }

        var t = Math.Max(0, Math.Min(1, ((px - x1) * dx + (py - y1) * dy) / lengthSq));
        var projX = x1 + t * dx;
        var projY = y1 + t * dy;

        return Math.Sqrt((px - projX) * (px - projX) + (py - projY) * (py - projY));
    }

    /// <summary>
    /// Called when starting to drag a component.
    /// </summary>
    public void StartMoveComponent(ComponentViewModel component)
    {
        _movingComponent = component;
        _moveStartX = component.X;
        _moveStartY = component.Y;
        Canvas.BeginDragComponent(component);
    }

    /// <summary>
    /// Called when starting to drag multiple components as a group.
    /// </summary>
    public void StartGroupMove(IEnumerable<ComponentViewModel> components)
    {
        _groupMoveStartPositions = new Dictionary<ComponentViewModel, (double x, double y)>();
        foreach (var comp in components)
        {
            _groupMoveStartPositions[comp] = (comp.X, comp.Y);
        }

        var firstComp = components.FirstOrDefault();
        if (firstComp != null)
        {
            Canvas.BeginDragComponent(firstComp);
        }
    }

    /// <summary>
    /// Called when finished dragging a component.
    /// </summary>
    public void EndMoveComponent()
    {
        if (_movingComponent != null)
        {
            Canvas.EndDragComponent(_movingComponent);

            if (Math.Abs(_movingComponent.X - _moveStartX) > 0.001 ||
                Math.Abs(_movingComponent.Y - _moveStartY) > 0.001)
            {
                var cmd = new MoveComponentCommand(
                    Canvas,
                    _movingComponent,
                    _moveStartX,
                    _moveStartY,
                    _movingComponent.X,
                    _movingComponent.Y);
                CommandManager.ExecuteCommand(cmd);
            }
        }
        _movingComponent = null;
    }

    /// <summary>
    /// Called when finished dragging multiple components as a group.
    /// </summary>
    public void EndGroupMove(IEnumerable<ComponentViewModel> components)
    {
        if (_groupMoveStartPositions == null || !_groupMoveStartPositions.Any())
            return;

        var firstComp = _groupMoveStartPositions.Keys.FirstOrDefault();
        if (firstComp == null)
            return;

        Canvas.EndDragComponent(firstComp);

        var startPos = _groupMoveStartPositions[firstComp];
        double deltaX = firstComp.X - startPos.x;
        double deltaY = firstComp.Y - startPos.y;

        if (Math.Abs(deltaX) > 0.001 || Math.Abs(deltaY) > 0.001)
        {
            var cmd = new GroupMoveCommand(
                Canvas,
                _groupMoveStartPositions.Keys.ToList(),
                deltaX,
                deltaY);
            CommandManager.ExecuteCommand(cmd);
        }

        _groupMoveStartPositions = null;
    }

    [RelayCommand]
    private void SetSelectMode()
    {
        CurrentMode = InteractionMode.Select;
        LeftPanel.SelectedTemplate = null;
        _connectionStartPin = null;
    }

    [RelayCommand]
    private void SetConnectMode()
    {
        CurrentMode = InteractionMode.Connect;
        LeftPanel.SelectedTemplate = null;
        _connectionStartPin = null;
        BottomPanel.StatusText = "Connect mode: Click on a pin to start connection";
    }

    [RelayCommand]
    private void SetDeleteMode()
    {
        CurrentMode = InteractionMode.Delete;
        LeftPanel.SelectedTemplate = null;
        _connectionStartPin = null;
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        var selection = Canvas.Selection;

        if (selection.HasMultipleSelected)
        {
            int count = selection.SelectedComponents.Count;
            var cmd = new GroupDeleteCommand(Canvas, selection.SelectedComponents.ToList());
            CommandManager.ExecuteCommand(cmd);
            selection.ClearSelection();
            RightPanel.SelectedComponent = null;
            BottomPanel.StatusText = $"Deleted {count} components";
            return;
        }

        if (RightPanel.SelectedComponent != null)
        {
            var name = RightPanel.SelectedComponent.Name;
            var cmd = new DeleteComponentCommand(Canvas, RightPanel.SelectedComponent);
            CommandManager.ExecuteCommand(cmd);
            selection.ClearSelection();
            RightPanel.SelectedComponent = null;
            BottomPanel.StatusText = $"Deleted: {name}";
        }
    }

    [RelayCommand]
    private void CopySelected()
    {
        var selection = Canvas.Selection;
        if (!selection.HasSelection) return;

        Canvas.Clipboard.Copy(
            selection.SelectedComponents.ToList(),
            Canvas.Connections);

        BottomPanel.StatusText = $"Copied {selection.SelectedComponents.Count} component(s)";
    }

    /// <summary>
    /// Pastes components from clipboard at the specified position.
    /// </summary>
    public void PasteSelected(double? targetX = null, double? targetY = null)
    {
        if (!Canvas.Clipboard.HasContent) return;

        var cmd = new PasteComponentsCommand(Canvas, Canvas.Clipboard, targetX, targetY);
        CommandManager.ExecuteCommand(cmd);

        if (cmd.Result != null)
        {
            Canvas.Selection.ClearSelection();
            foreach (var comp in cmd.Result.Components)
            {
                comp.IsSelected = true;
                Canvas.Selection.SelectedComponents.Add(comp);
            }

            _ = Canvas.RecalculateRoutesAsync();
            BottomPanel.StatusText = $"Pasted {cmd.Result.Components.Count} component(s)";
        }
    }

    [RelayCommand]
    private void PasteSelectedCommand()
    {
        PasteSelected();
    }

    /// <summary>
    /// Creates a component group from the currently selected components.
    /// Requires at least 2 components to be selected.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCreateGroup))]
    private void CreateGroup()
    {
        var selection = Canvas.Selection;
        if (!selection.HasMultipleSelected)
            return;

        // Prompt for group name (for now, use a simple default name)
        // In a full implementation, this would show a dialog
        var groupName = $"Group {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        var category = "User Defined";
        var description = "";

        var cmd = new CreateGroupCommand(
            Canvas,
            LeftPanel.ComponentGroups,
            selection.SelectedComponents.ToList(),
            groupName,
            category,
            description);

        CommandManager.ExecuteCommand(cmd);
        BottomPanel.StatusText = $"Created group '{groupName}' with {selection.SelectedComponents.Count} components";
    }

    private bool CanCreateGroup()
    {
        // Requires at least 2 components selected
        return Canvas.Selection.SelectedComponents.Count >= 2;
    }

    [RelayCommand]
    private void RotateSelected()
    {
        if (RightPanel.SelectedComponent != null)
        {
            var cmd = new RotateComponentCommand(Canvas, RightPanel.SelectedComponent);
            CommandManager.ExecuteCommand(cmd);
            BottomPanel.StatusText = cmd.WasApplied
                ? $"Rotated: {RightPanel.SelectedComponent.Name}"
                : $"Cannot rotate: {RightPanel.SelectedComponent.Name} would overlap another component";
        }
    }

    [RelayCommand]
    private async Task RunSimulation()
    {
        if (_isSimulating) return;

        if (Canvas.ShowPowerFlow)
        {
            Canvas.ShowPowerFlow = false;
            Canvas.PowerFlowVisualizer.IsEnabled = false;
            BottomPanel.StatusText = "Simulation overlay OFF";
            return;
        }

        await ExecuteSimulation();
    }

    /// <summary>
    /// Runs simulation without toggle logic. Used by auto-resimulation.
    /// </summary>
    private async Task ExecuteSimulation()
    {
        if (_isSimulating) return;
        _isSimulating = true;

        try
        {
            BottomPanel.StatusText = "Running simulation...";
            var result = await Simulation.RunAsync(Canvas);

            if (result.Success)
            {
                BottomPanel.StatusText = $"Simulation complete: {result.LightSourceCount} source(s), " +
                             $"{result.ConnectionCount} connections @ {result.WavelengthSummary}";

                if (result.SystemMatrix != null)
                {
                    RightPanel.SMatrixPerformance.AnalyzeMatrix(result.SystemMatrix);
                }
            }
            else
            {
                BottomPanel.StatusText = result.ErrorMessage ?? "Simulation failed";
            }
        }
        catch (Exception ex)
        {
            BottomPanel.StatusText = $"Simulation error: {ex.Message}";
        }
        finally
        {
            _isSimulating = false;
        }
    }

    [RelayCommand]
    private void Undo()
    {
        if (CommandManager.Undo())
        {
            BottomPanel.StatusText = $"Undone: {CommandManager.RedoDescription ?? "action"}";
        }
        else
        {
            BottomPanel.StatusText = "Nothing to undo";
        }
    }

    [RelayCommand]
    private void Redo()
    {
        if (CommandManager.Redo())
        {
            BottomPanel.StatusText = $"Redone: {CommandManager.UndoDescription ?? "action"}";
        }
        else
        {
            BottomPanel.StatusText = "Nothing to redo";
        }
    }

    [RelayCommand]
    private void ZoomIn()
    {
        ZoomLevel = Math.Min(ZoomLevel * 1.2, 10.0);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        ZoomLevel = Math.Max(ZoomLevel / 1.2, 0.1);
    }

    [RelayCommand]
    private void ResetZoom()
    {
        ZoomLevel = 1.0;
    }

    [RelayCommand]
    private void ResetPan()
    {
        Canvas.PanX = 0;
        Canvas.PanY = 0;
    }

    [RelayCommand]
    private void RunDesignChecks()
    {
        var connections = Canvas.Connections
            .Select(c => c.Connection)
            .ToList();

        RightPanel.DesignValidation.RunValidation(connections);
        BottomPanel.StatusText = RightPanel.DesignValidation.StatusText;
    }

    private void WireDesignValidation()
    {
        RightPanel.DesignValidation.NavigateToPosition = (x, y) =>
        {
            NavigateCanvasTo(x, y);
        };

        RightPanel.DesignValidation.HighlightConnection = (connection) =>
        {
            foreach (var conn in Canvas.Connections)
            {
                conn.IsSelected = conn.Connection == connection;
            }
        };
    }

    private void NavigateCanvasTo(double centerX, double centerY)
    {
        var (vpWidth, vpHeight) = GetViewportSize?.Invoke() ?? (900, 800);

        Canvas.PanX = vpWidth / 2 - centerX * ZoomLevel;
        Canvas.PanY = vpHeight / 2 - centerY * ZoomLevel;
    }

    /// <summary>
    /// Adjusts zoom and pan to fit all components in the viewport.
    /// </summary>
    public void ZoomToFit(double viewportWidth, double viewportHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        var bounds = BoundingBoxCalculator.Calculate(Canvas.Components);
        if (bounds == null)
        {
            BottomPanel.StatusText = "No components to fit";
            return;
        }

        var padded = BoundingBoxCalculator.WithPadding(
            bounds.Value, BoundingBoxCalculator.DefaultPaddingFraction);

        if (padded.IsEmpty) return;

        var (zoom, panX, panY) = BoundingBoxCalculator.CalculateZoomToFit(
            padded, viewportWidth, viewportHeight);

        ZoomLevel = zoom;
        Canvas.PanX = panX;
        Canvas.PanY = panY;
        BottomPanel.StatusText = $"Zoom to fit: {zoom:P0}";
    }

    [RelayCommand]
    private async Task LoadPdk()
    {
        if (FileDialogService == null) return;

        var filePath = await FileDialogService.ShowOpenFileDialogAsync(
            "Open PDK",
            "PDK Files (*.json)|*.json|All Files (*.*)|*.*");

        if (string.IsNullOrEmpty(filePath)) return;

        if (LeftPanel.PdkManager.IsPdkLoaded(filePath))
        {
            BottomPanel.StatusText = "PDK already loaded from this file";
            return;
        }

        try
        {
            var pdk = _pdkLoader.LoadFromFile(filePath);

            if (LeftPanel.PdkManager.IsPdkNameLoaded(pdk.Name, null))
            {
                BottomPanel.StatusText = $"PDK '{pdk.Name}' is already loaded";
                return;
            }

            int addedCount = 0;
            foreach (var pdkComp in pdk.Components)
            {
                var template = ConvertPdkComponentToTemplate(pdkComp, pdk.Name, pdk.NazcaModuleName);
                LeftPanel.ComponentLibrary.Add(template);
                if (!LeftPanel.Categories.Contains(template.Category))
                    LeftPanel.Categories.Add(template.Category);
                addedCount++;
            }

            LeftPanel.PdkManager.RegisterPdk(pdk.Name, filePath, false, addedCount);
            _preferencesService.AddUserPdkPath(filePath);

            FilterComponents();
            BottomPanel.StatusText = $"Loaded PDK '{pdk.Name}' with {addedCount} components";
        }
        catch (Exception ex)
        {
            BottomPanel.StatusText = $"Failed to load PDK: {ex.Message}";
        }
    }

    private ComponentTemplate ConvertPdkComponentToTemplate(PdkComponentDraft pdkComp, string pdkName = "PDK", string? nazcaModuleName = null)
    {
        var pinDefs = pdkComp.Pins.Select(p => new PinDefinition(
            p.Name,
            p.OffsetXMicrometers,
            p.OffsetYMicrometers,
            p.AngleDegrees
        )).ToArray();

        var firstPin = pdkComp.Pins.FirstOrDefault();
        double nazcaOriginOffsetX = firstPin?.OffsetXMicrometers ?? 0;
        double nazcaOriginOffsetY = firstPin?.OffsetYMicrometers ?? 0;

        var template = new ComponentTemplate
        {
            Name = pdkComp.Name,
            Category = pdkComp.Category,
            WidthMicrometers = pdkComp.WidthMicrometers,
            HeightMicrometers = pdkComp.HeightMicrometers,
            PinDefinitions = pinDefs,
            NazcaFunctionName = pdkComp.NazcaFunction,
            NazcaParameters = pdkComp.NazcaParameters,
            HasSlider = pdkComp.Sliders?.Any() ?? false,
            SliderMin = pdkComp.Sliders?.FirstOrDefault()?.MinVal ?? 0,
            SliderMax = pdkComp.Sliders?.FirstOrDefault()?.MaxVal ?? 100,
            PdkSource = pdkName,
            NazcaModuleName = nazcaModuleName,
            NazcaOriginOffsetX = nazcaOriginOffsetX,
            NazcaOriginOffsetY = nazcaOriginOffsetY,
        };

        if (pdkComp.SMatrix?.WavelengthData is { Count: > 0 } wlData)
        {
            template.CreateWavelengthSMatrixMap = pins =>
            {
                var map = new Dictionary<int, CAP_Core.LightCalculation.SMatrix>();
                foreach (var entry in wlData)
                {
                    var draft = new PdkSMatrixDraft
                    {
                        WavelengthNm = entry.WavelengthNm,
                        Connections = entry.Connections
                    };
                    map[entry.WavelengthNm] = CreateSMatrixFromPdk(pins, draft);
                }
                return map;
            };
        }
        else
        {
            template.CreateSMatrix = pins => CreateSMatrixFromPdk(pins, pdkComp.SMatrix);
        }

        return template;
    }

    private static CAP_Core.LightCalculation.SMatrix CreateSMatrixFromPdk(
        List<Pin> pins,
        PdkSMatrixDraft? sMatrixDraft)
    {
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var sMatrix = new CAP_Core.LightCalculation.SMatrix(pinIds, new List<(Guid, double)>());

        if (sMatrixDraft?.Connections == null || sMatrixDraft.Connections.Count == 0)
            return sMatrix;

        var pinByName = new Dictionary<string, Pin>(StringComparer.OrdinalIgnoreCase);
        foreach (var pin in pins)
        {
            pinByName[pin.Name] = pin;
        }

        var transfers = new Dictionary<(Guid, Guid), System.Numerics.Complex>();

        foreach (var conn in sMatrixDraft.Connections)
        {
            if (!pinByName.TryGetValue(conn.FromPin, out var fromPin) ||
                !pinByName.TryGetValue(conn.ToPin, out var toPin))
                continue;

            var phaseRad = conn.PhaseDegrees * Math.PI / 180.0;
            var value = System.Numerics.Complex.FromPolarCoordinates(conn.Magnitude, phaseRad);

            transfers[(fromPin.IDInFlow, toPin.IDOutFlow)] = value;
            transfers[(toPin.IDInFlow, fromPin.IDOutFlow)] = value;
        }

        sMatrix.SetValues(transfers);
        return sMatrix;
    }

    [RelayCommand]
    private async Task ExportNazca()
    {
        if (FileDialogService == null)
        {
            BottomPanel.StatusText = "Export not available";
            return;
        }

        if (Canvas.Components.Count == 0)
        {
            BottomPanel.StatusText = "Nothing to export - add some components first";
            return;
        }

        var filePath = await FileDialogService.ShowSaveFileDialogAsync(
            "Export to Nazca Python",
            "py",
            "Python Files|*.py|All Files|*.*");

        if (filePath != null)
        {
            try
            {
                var nazcaCode = _nazcaExporter.Export(Canvas);
                await File.WriteAllTextAsync(filePath, nazcaCode);
                BottomPanel.StatusText = $"Exported to {Path.GetFileName(filePath)}";
            }
            catch (Exception ex)
            {
                BottomPanel.StatusText = $"Export failed: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    private async Task SaveDesign()
    {
        if (FileDialogService == null)
        {
            BottomPanel.StatusText = "Save not available";
            return;
        }

        var filePath = _currentFilePath ?? await FileDialogService.ShowSaveFileDialogAsync(
            "Save Design",
            "cappro",
            "Connect-A-PIC Pro Files|*.cappro|All Files|*.*");

        if (filePath != null)
        {
            await SaveToFile(filePath);
        }
    }

    [RelayCommand]
    private async Task SaveDesignAs()
    {
        if (FileDialogService == null)
        {
            BottomPanel.StatusText = "Save not available";
            return;
        }

        var filePath = await FileDialogService.ShowSaveFileDialogAsync(
            "Save Design As",
            "cappro",
            "Connect-A-PIC Pro Files|*.cappro|All Files|*.*");

        if (filePath != null)
        {
            await SaveToFile(filePath);
        }
    }

    private async Task SaveToFile(string filePath)
    {
        try
        {
            var designData = new DesignFileData
            {
                Components = Canvas.Components.Select(c => new ComponentData
                {
                    TemplateName = c.TemplateName ?? c.Name,
                    X = c.X,
                    Y = c.Y,
                    Identifier = c.Component.Identifier,
                    Rotation = (int)c.Component.Rotation90CounterClock,
                    SliderValue = c.HasSliders ? c.SliderValue : null,
                    LaserWavelengthNm = c.LaserConfig?.WavelengthNm,
                    LaserPower = c.LaserConfig?.InputPower,
                    IsLocked = c.Component.IsLocked ? true : null
                }).ToList(),
                Connections = Canvas.Connections.Select(c => new ConnectionData
                {
                    StartComponentIndex = Canvas.Components.ToList().FindIndex(
                        comp => comp.Component == c.Connection.StartPin.ParentComponent),
                    StartPinName = c.Connection.StartPin.Name,
                    EndComponentIndex = Canvas.Components.ToList().FindIndex(
                        comp => comp.Component == c.Connection.EndPin.ParentComponent),
                    EndPinName = c.Connection.EndPin.Name,
                    CachedSegments = c.Connection.RoutedPath != null
                        ? PathSegmentConverter.ToDtoList(c.Connection.RoutedPath.Segments)
                        : null,
                    IsBlockedFallback = c.Connection.IsBlockedFallback ? true : null,
                    IsLocked = c.Connection.IsLocked ? true : null,
                    TargetLengthMicrometers = c.Connection.TargetLengthMicrometers,
                    IsTargetLengthEnabled = c.Connection.IsTargetLengthEnabled ? true : null,
                    LengthToleranceMicrometers = c.Connection.IsTargetLengthEnabled ? c.Connection.LengthToleranceMicrometers : null
                }).ToList()
            };

            var json = JsonSerializer.Serialize(designData, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            await File.WriteAllTextAsync(filePath, json);
            _currentFilePath = filePath;
            BottomPanel.StatusText = $"Saved to {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            BottomPanel.StatusText = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadDesign()
    {
        if (FileDialogService == null)
        {
            BottomPanel.StatusText = "Load not available";
            return;
        }

        var filePath = await FileDialogService.ShowOpenFileDialogAsync(
            "Load Design",
            "Connect-A-PIC Pro Files|*.cappro|All Files|*.*");

        if (filePath != null)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var designData = JsonSerializer.Deserialize<DesignFileData>(json);

                if (designData == null)
                {
                    BottomPanel.StatusText = "Invalid design file";
                    return;
                }

                Canvas.Components.Clear();
                Canvas.Connections.Clear();
                Canvas.ConnectionManager.Clear();
                CommandManager.ClearHistory();

                foreach (var compData in designData.Components)
                {
                    var template = LeftPanel.ComponentLibrary.FirstOrDefault(t =>
                        t.Name.Equals(compData.TemplateName, StringComparison.OrdinalIgnoreCase));

                    if (template != null)
                    {
                        var component = ComponentTemplates.CreateFromTemplate(template, compData.X, compData.Y);

                        for (int i = 0; i < compData.Rotation; i++)
                        {
                            ApplyRotationToComponent(component);
                        }

                        var vm = Canvas.AddComponent(component, template.Name);

                        if (compData.SliderValue.HasValue && vm.HasSliders)
                            vm.SliderValue = compData.SliderValue.Value;

                        if (vm.LaserConfig != null)
                        {
                            if (compData.LaserWavelengthNm.HasValue)
                                vm.LaserConfig.WavelengthNm = compData.LaserWavelengthNm.Value;
                            if (compData.LaserPower.HasValue)
                                vm.LaserConfig.InputPower = compData.LaserPower.Value;
                        }

                        if (compData.IsLocked == true)
                            component.IsLocked = true;
                    }
                }

                foreach (var connData in designData.Connections)
                {
                    if (connData.StartComponentIndex >= 0 && connData.StartComponentIndex < Canvas.Components.Count &&
                        connData.EndComponentIndex >= 0 && connData.EndComponentIndex < Canvas.Components.Count)
                    {
                        var startComp = Canvas.Components[connData.StartComponentIndex];
                        var endComp = Canvas.Components[connData.EndComponentIndex];

                        var startPin = startComp.Component.PhysicalPins
                            .FirstOrDefault(p => p.Name == connData.StartPinName);
                        var endPin = endComp.Component.PhysicalPins
                            .FirstOrDefault(p => p.Name == connData.EndPinName);

                        if (startPin != null && endPin != null)
                        {
                            var cachedPath = PathSegmentConverter.ToRoutedPath(
                                connData.CachedSegments, connData.IsBlockedFallback ?? false);

                            WaveguideConnectionViewModel? connVm = null;

                            if (cachedPath != null && cachedPath.IsValid)
                            {
                                connVm = Canvas.ConnectPinsWithCachedRoute(startPin, endPin, cachedPath);
                            }
                            else
                            {
                                connVm = Canvas.ConnectPins(startPin, endPin);
                            }

                            if (connVm != null && connData.IsLocked == true)
                            {
                                connVm.Connection.IsLocked = true;
                            }

                            if (connVm != null)
                            {
                                if (connData.TargetLengthMicrometers.HasValue)
                                    connVm.Connection.TargetLengthMicrometers = connData.TargetLengthMicrometers.Value;
                                if (connData.IsTargetLengthEnabled == true)
                                    connVm.Connection.IsTargetLengthEnabled = true;
                                if (connData.LengthToleranceMicrometers.HasValue)
                                    connVm.Connection.LengthToleranceMicrometers = connData.LengthToleranceMicrometers.Value;
                            }
                        }
                    }
                }

                foreach (var conn in Canvas.Connections)
                {
                    conn.NotifyPathChanged();
                }

                _currentFilePath = filePath;
                BottomPanel.StatusText = $"Loaded {Path.GetFileName(filePath)} ({Canvas.Components.Count} components, {Canvas.Connections.Count} connections)";
                CommandManager.NotifyStateChanged();

                var (vpWidth, vpHeight) = GetViewportSize?.Invoke() ?? (900, 800);
                ZoomToFit(vpWidth, vpHeight);
            }
            catch (Exception ex)
            {
                BottomPanel.StatusText = $"Load failed: {ex.Message}";
            }
        }
    }

    private static void ApplyRotationToComponent(Component comp)
    {
        var width = comp.WidthMicrometers;
        var height = comp.HeightMicrometers;

        foreach (var pin in comp.PhysicalPins)
        {
            var cx = width / 2;
            var cy = height / 2;
            var x = pin.OffsetXMicrometers - cx;
            var y = pin.OffsetYMicrometers - cy;
            var newX = -y;
            var newY = x;
            pin.OffsetXMicrometers = newX + cy;
            pin.OffsetYMicrometers = newY + cx;
        }

        comp.WidthMicrometers = height;
        comp.HeightMicrometers = width;
        comp.RotateBy90CounterClockwise();
    }
}

// Data classes for serialization
public class DesignFileData
{
    public List<ComponentData> Components { get; set; } = new();
    public List<ConnectionData> Connections { get; set; } = new();
}

public class ComponentData
{
    public string TemplateName { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public string Identifier { get; set; } = "";
    public int Rotation { get; set; }
    public double? SliderValue { get; set; }
    public int? LaserWavelengthNm { get; set; }
    public double? LaserPower { get; set; }
    public bool? IsLocked { get; set; }
}

public class ConnectionData
{
    public int StartComponentIndex { get; set; }
    public string StartPinName { get; set; } = "";
    public int EndComponentIndex { get; set; }
    public string EndPinName { get; set; } = "";
    public List<PathSegmentData>? CachedSegments { get; set; }
    public bool? IsBlockedFallback { get; set; }
    public bool? IsLocked { get; set; }
    public double? TargetLengthMicrometers { get; set; }
    public bool? IsTargetLengthEnabled { get; set; }
    public double? LengthToleranceMicrometers { get; set; }
}

public class PathSegmentData
{
    public string Type { get; set; } = "";
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double EndX { get; set; }
    public double EndY { get; set; }
    public double StartAngleDegrees { get; set; }
    public double EndAngleDegrees { get; set; }
    public double? CenterX { get; set; }
    public double? CenterY { get; set; }
    public double? RadiusMicrometers { get; set; }
    public double? SweepAngleDegrees { get; set; }
}
