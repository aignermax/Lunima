using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Components.Core;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Simulation;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Hierarchy;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// Main ViewModel that orchestrates all panel ViewModels.
/// Refactored to ~250 lines following CLAUDE.md guidelines.
/// Delegates responsibilities to specialized panel ViewModels.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private DesignCanvasViewModel _canvas;

    [ObservableProperty]
    private string _statusText = "Ready";

    public Commands.CommandManager CommandManager { get; }
    public SimulationService Simulation { get; }

    /// <summary>
    /// ViewModel for canvas interaction (selection, placement, connections).
    /// </summary>
    public CanvasInteractionViewModel CanvasInteraction { get; }

    /// <summary>
    /// ViewModel for file operations (save, load, export).
    /// </summary>
    public FileOperationsViewModel FileOperations { get; }

    /// <summary>
    /// ViewModel for viewport control (zoom, pan, navigation).
    /// </summary>
    public ViewportControlViewModel ViewportControl { get; }

    /// <summary>
    /// ViewModel for the left sidebar panel (component library, PDK management).
    /// </summary>
    public LeftPanelViewModel LeftPanel { get; }

    /// <summary>
    /// ViewModel for the right sidebar panel (analysis, diagnostics, validation).
    /// </summary>
    public RightPanelViewModel RightPanel { get; }

    /// <summary>
    /// ViewModel for the bottom panel (waveguide length, element locking, status).
    /// </summary>
    public BottomPanelViewModel BottomPanel { get; }

    // Backward-compatible properties - delegate to Panel ViewModels
    // TODO: Update XAML bindings and remove these properties
    public ParameterSweepViewModel Sweep => RightPanel.Sweep;
    public RoutingDiagnosticsViewModel RoutingDiagnostics => RightPanel.RoutingDiagnostics;
    public DesignValidationViewModel DesignValidation => RightPanel.DesignValidation;
    public PdkManagerViewModel PdkManager => LeftPanel.PdkManager;
    public ComponentDimensionDiagnosticsViewModel? DimensionDiagnostics => RightPanel.DimensionDiagnostics;
    public ElementLockViewModel ElementLock => BottomPanel.ElementLock;
    public ComponentDimensionViewModel DimensionValidator => RightPanel.DimensionValidator;
    public ExportValidationViewModel ExportValidation => RightPanel.ExportValidation;
    public SMatrixPerformanceViewModel SMatrixPerformance => RightPanel.SMatrixPerformance;
    public CompressLayoutViewModel CompressLayout => RightPanel.CompressLayout;
    public WaveguideLengthViewModel WaveguideLength => BottomPanel.WaveguideLength;
    public HierarchyPanelViewModel HierarchyPanel => LeftPanel.HierarchyPanel;
    public ComponentLibraryViewModel GroupLibrary => LeftPanel.ComponentLibrary;

    // Backward-compatible library properties
    public ObservableCollection<ComponentTemplate> ComponentLibrary => LeftPanel.AllTemplates;
    public ObservableCollection<ComponentTemplate> FilteredComponentLibrary => LeftPanel.FilteredTemplates;
    public ObservableCollection<string> Categories => LeftPanel.Categories;
    public string SearchText
    {
        get => LeftPanel.SearchText;
        set => LeftPanel.SearchText = value;
    }

    // Backward-compatible interaction properties
    public InteractionMode CurrentMode
    {
        get => CanvasInteraction.CurrentMode;
        set => CanvasInteraction.CurrentMode = value;
    }
    public ComponentTemplate? SelectedTemplate
    {
        get => CanvasInteraction.SelectedTemplate;
        set => CanvasInteraction.SelectedTemplate = value;
    }
    public ComponentViewModel? SelectedComponent
    {
        get => CanvasInteraction.SelectedComponent;
        set => CanvasInteraction.SelectedComponent = value;
    }
    public WaveguideConnectionViewModel? SelectedWaveguideConnection
    {
        get => CanvasInteraction.SelectedWaveguideConnection;
        set => CanvasInteraction.SelectedWaveguideConnection = value;
    }

    // Backward-compatible zoom property
    public double ZoomLevel
    {
        get => ViewportControl.ZoomLevel;
        set => ViewportControl.ZoomLevel = value;
    }

    /// <summary>
    /// Available wavelength options for the laser configuration dropdown.
    /// </summary>
    public IReadOnlyList<WavelengthOption> WavelengthOptions { get; } = WavelengthOption.All;

    public IFileDialogService? FileDialogService
    {
        get => FileOperations.FileDialogService;
        set
        {
            FileOperations.FileDialogService = value;
            LeftPanel.FileDialogService = value;
        }
    }

    private bool _isSimulating;

    public MainViewModel(
        SimulationService simulationService,
        SimpleNazcaExporter nazcaExporter,
        PdkLoader pdkLoader,
        Commands.CommandManager commandManager,
        UserPreferencesService preferencesService,
        CAP_Core.Components.Creation.GroupLibraryManager groupLibraryManager,
        Services.GroupPreviewGenerator previewGenerator)
    {
        Simulation = simulationService;
        CommandManager = commandManager;
        _canvas = new DesignCanvasViewModel();
        _canvas.SimulationRequested = async () => await ExecuteSimulation();

        // Initialize Panel ViewModels (order matters due to dependencies)
        LeftPanel = new LeftPanelViewModel(_canvas, groupLibraryManager, pdkLoader, preferencesService);
        CanvasInteraction = new CanvasInteractionViewModel(_canvas, commandManager, LeftPanel.ComponentLibrary, previewGenerator);
        FileOperations = new FileOperationsViewModel(_canvas, commandManager, nazcaExporter, LeftPanel.AllTemplates);
        ViewportControl = new ViewportControlViewModel(_canvas);
        RightPanel = new RightPanelViewModel(_canvas);
        BottomPanel = new BottomPanelViewModel(_canvas, CommandManager);

        // Wire up status callbacks
        CanvasInteraction.UpdateStatus = UpdateStatusText;
        FileOperations.UpdateStatus = UpdateStatusText;
        ViewportControl.UpdateStatus = UpdateStatusText;
        LeftPanel.UpdateStatus = UpdateStatusText;

        // Wire up canvas status updates to bottom panel
        _canvas.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(DesignCanvasViewModel.RoutingStatusText))
            {
                var routingText = _canvas.RoutingStatusText;
                if (!string.IsNullOrEmpty(routingText))
                {
                    UpdateStatusText(routingText);
                }
            }
        };

        // Wire up callbacks
        CanvasInteraction.OnSelectionChanged = comp =>
        {
            Sweep.ConfigureForComponent(comp, Canvas);
            HierarchyPanel.SyncSelectionFromCanvas(comp);
        };

        WireDesignValidation();
        WireHierarchyPanel();
        WireFileOperations();

        // Initialize component library
        LeftPanel.Initialize();
    }

    private void UpdateStatusText(string text)
    {
        StatusText = text;
        BottomPanel.SetStatus(text);
    }

    private void WireHierarchyPanel()
    {
        HierarchyPanel.NavigateToPosition = ViewportControl.NavigateCanvasTo;
        HierarchyPanel.GetViewportSize = ViewportControl.GetViewportSize;
    }

    private void WireDesignValidation()
    {
        DesignValidation.NavigateToPosition = ViewportControl.NavigateCanvasTo;
        DesignValidation.HighlightConnection = (connection) =>
        {
            foreach (var conn in Canvas.Connections)
            {
                conn.IsSelected = conn.Connection == connection;
            }
        };
    }

    private void WireFileOperations()
    {
        FileOperations.RebuildHierarchy = HierarchyPanel.RebuildTree;
        FileOperations.ZoomToFitAfterLoad = (w, h) =>
        {
            var (vpWidth, vpHeight) = ViewportControl.GetViewportSize?.Invoke() ?? (w, h);
            ViewportControl.ZoomToFit(vpWidth, vpHeight);
        };
    }

    // Canvas interaction delegates
    public void CanvasClicked(double x, double y) => CanvasInteraction.CanvasClicked(x, y);
    public void PinClicked(PhysicalPin pin) => CanvasInteraction.PinClicked(pin);
    public void CanvasMouseMove(double x, double y) => CanvasInteraction.CanvasMouseMove(x, y);
    public void StartMoveComponent(ComponentViewModel component) => CanvasInteraction.StartMoveComponent(component);
    public void EndMoveComponent() => CanvasInteraction.EndMoveComponent();
    public void StartGroupMove(IEnumerable<ComponentViewModel> components) => CanvasInteraction.StartGroupMove(components);
    public void EndGroupMove(IEnumerable<ComponentViewModel> components) => CanvasInteraction.EndGroupMove(components);
    public void PasteSelected(double? targetX = null, double? targetY = null) => CanvasInteraction.PasteSelected(targetX, targetY);

    // Viewport control delegates
    public void ZoomToFit(double viewportWidth, double viewportHeight) => ViewportControl.ZoomToFit(viewportWidth, viewportHeight);

    // Backward-compatible command delegates
    [RelayCommand]
    private void SetSelectMode() => CanvasInteraction.SetSelectModeCommand.Execute(null);

    [RelayCommand]
    private void SetConnectMode() => CanvasInteraction.SetConnectModeCommand.Execute(null);

    [RelayCommand]
    private void SetDeleteMode() => CanvasInteraction.SetDeleteModeCommand.Execute(null);

    [RelayCommand]
    private void DeleteSelected() => CanvasInteraction.DeleteSelectedCommand.Execute(null);

    [RelayCommand]
    private void CopySelected() => CanvasInteraction.CopySelectedCommand.Execute(null);

    [RelayCommand]
    private void PasteSelectedCommand() => CanvasInteraction.PasteSelectedCommandCommand.Execute(null);

    [RelayCommand]
    private void RotateSelected() => CanvasInteraction.RotateSelectedCommand.Execute(null);

    [RelayCommand]
    private void CreateGroup() => CanvasInteraction.CreateGroupCommand.Execute(null);

    [RelayCommand]
    private void Ungroup() => CanvasInteraction.UngroupCommand.Execute(null);

    [RelayCommand]
    private void ZoomIn() => ViewportControl.ZoomInCommand.Execute(null);

    [RelayCommand]
    private void ZoomOut() => ViewportControl.ZoomOutCommand.Execute(null);

    [RelayCommand]
    private void ResetZoom() => ViewportControl.ResetZoomCommand.Execute(null);

    [RelayCommand]
    private void ResetPan() => ViewportControl.ResetPanCommand.Execute(null);

    [RelayCommand]
    private async Task SaveDesign() => await FileOperations.SaveDesignCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task SaveDesignAs() => await FileOperations.SaveDesignAsCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task LoadDesign() => await FileOperations.LoadDesignCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task ExportNazca() => await FileOperations.ExportNazcaCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task LoadPdk() => await LeftPanel.LoadPdkCommand.ExecuteAsync(null);

    [RelayCommand]
    private void Undo()
    {
        if (CommandManager.Undo())
        {
            StatusText = $"Undone: {CommandManager.RedoDescription ?? "action"}";
        }
        else
        {
            StatusText = "Nothing to undo";
        }
    }

    [RelayCommand]
    private void Redo()
    {
        if (CommandManager.Redo())
        {
            StatusText = $"Redone: {CommandManager.UndoDescription ?? "action"}";
        }
        else
        {
            StatusText = "Nothing to redo";
        }
    }

    [RelayCommand]
    private async Task RunSimulation()
    {
        if (_isSimulating) return;

        // Toggle off if overlay is already showing
        if (Canvas.ShowPowerFlow)
        {
            Canvas.ShowPowerFlow = false;
            Canvas.PowerFlowVisualizer.IsEnabled = false;
            StatusText = "Simulation overlay OFF";
            return;
        }

        await ExecuteSimulation();
    }

    /// <summary>
    /// Runs simulation without toggle logic (used by auto-resimulation).
    /// </summary>
    private async Task ExecuteSimulation()
    {
        if (_isSimulating) return;
        _isSimulating = true;

        try
        {
            StatusText = "Running simulation...";
            var result = await Simulation.RunAsync(Canvas);

            if (result.Success)
            {
                StatusText = $"Simulation complete: {result.LightSourceCount} source(s), " +
                             $"{result.ConnectionCount} connections @ {result.WavelengthSummary}";

                if (result.SystemMatrix != null)
                {
                    SMatrixPerformance.AnalyzeMatrix(result.SystemMatrix);
                }
            }
            else
            {
                StatusText = result.ErrorMessage ?? "Simulation failed";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Simulation error: {ex.Message}";
        }
        finally
        {
            _isSimulating = false;
        }
    }

    [RelayCommand]
    private void RunDesignChecks()
    {
        var connections = Canvas.Connections
            .Select(c => c.Connection)
            .ToList();

        DesignValidation.RunValidation(connections);
        StatusText = DesignValidation.StatusText;
    }
}

// Data classes for serialization (used by FileOperationsViewModel)
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

/// <summary>
/// DTO for serializing waveguide path segments.
/// </summary>
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
