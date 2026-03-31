using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Components.Core;
using CAP_Core;
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
using CAP.Avalonia.ViewModels.Export;

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
        DesignCanvasViewModel canvas,
        SimulationService simulationService,
        SimpleNazcaExporter nazcaExporter,
        Commands.CommandManager commandManager,
        UserPreferencesService preferencesService,
        Services.GroupPreviewGenerator previewGenerator,
        Services.IInputDialogService inputDialogService,
        CAP_Core.Export.GdsExportService gdsExportService,
        ErrorConsoleService errorConsoleService,
        LeftPanelViewModel leftPanel,
        RightPanelViewModel rightPanel,
        BottomPanelViewModel bottomPanel)
    {
        Simulation = simulationService;
        CommandManager = commandManager;
        _canvas = canvas;
        _canvas.SimulationRequested = async () => await ExecuteSimulation();

        // Wire panel ViewModels (injected via DI)
        LeftPanel = leftPanel;
        RightPanel = rightPanel;
        BottomPanel = bottomPanel;

        CanvasInteraction = new CanvasInteractionViewModel(_canvas, commandManager, LeftPanel.ComponentLibrary, previewGenerator, inputDialogService);
        var gdsExportVm = new ViewModels.Export.GdsExportViewModel(gdsExportService, errorConsoleService);
        gdsExportVm.Initialize(preferencesService.GetCustomPythonPath());
        gdsExportVm.OnPythonPathChanged = path => preferencesService.SetCustomPythonPath(path);
        FileOperations = new FileOperationsViewModel(_canvas, commandManager, nazcaExporter, LeftPanel.AllTemplates, gdsExportVm, errorConsoleService);
        ViewportControl = new ViewportControlViewModel(_canvas);

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
            RightPanel.Sweep.ConfigureForComponent(comp, Canvas);
            LeftPanel.HierarchyPanel.SyncSelectionFromCanvas(comp);
        };

        // Wire rename from hierarchy panel through undo-aware command manager
        LeftPanel.HierarchyPanel.RenameComponent = (component, newName) =>
        {
            var cmd = new Commands.RenameComponentCommand(component, newName);
            CommandManager.ExecuteCommand(cmd);
            LeftPanel.HierarchyPanel.RefreshNode(component);
        };

        CanvasInteraction.ClearLeftPanelGroupSelection = () =>
        {
            LeftPanel.SelectedGroupTemplate = null;
        };

        CanvasInteraction.ClearComponentTemplateSelection = () =>
        {
            CanvasInteraction.SelectedTemplate = null;
        };

        // Wire up mode changes and template selection to keep UI in sync
        CanvasInteraction.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(CanvasInteraction.CurrentMode))
            {
                var mode = CanvasInteraction.CurrentMode;
                // Deselect templates when switching away from placement modes
                if (mode != InteractionMode.PlaceComponent && mode != InteractionMode.PlaceGroupTemplate)
                {
                    LeftPanel.SelectedGroupTemplate = null;
                    // Note: SelectedTemplate is automatically cleared via CanvasInteraction.OnCurrentModeChanged
                }
            }
            else if (e.PropertyName == nameof(CanvasInteraction.SelectedTemplate))
            {
                // When a component template is selected, deselect group template in left panel
                if (CanvasInteraction.SelectedTemplate != null)
                {
                    LeftPanel.SelectedGroupTemplate = null;
                }
            }
            else if (e.PropertyName == nameof(CanvasInteraction.SelectedGroupTemplate))
            {
                // When a group template is selected, deselect component template
                // (SelectedTemplate is bound to MainViewModel.SelectedTemplate which wraps CanvasInteraction.SelectedTemplate,
                // so it will automatically update the UI ListBox)
            }
        };

        // Wire up group template selection from left panel to canvas interaction
        LeftPanel.OnGroupTemplateSelected = template =>
        {
            // Ensure TemplateGroup is loaded before setting as selected
            if (template.TemplateGroup == null && !string.IsNullOrEmpty(template.FilePath))
            {
                // Try to load the template group data from disk
                try
                {
                    if (System.IO.File.Exists(template.FilePath))
                    {
                        var json = System.IO.File.ReadAllText(template.FilePath);
                        var fileData = System.Text.Json.JsonSerializer.Deserialize<CAP_Core.Components.Creation.GroupLibraryFileData>(json);

                        if (fileData != null && !string.IsNullOrWhiteSpace(fileData.GroupData))
                        {
                            var group = CAP_Core.Components.Creation.GroupTemplateSerializer.Deserialize(fileData.GroupData);
                            if (group != null)
                            {
                                template.TemplateGroup = group;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    StatusText = $"Failed to load template '{template.Name}': {ex.Message}";
                    BottomPanel.ErrorConsole.Log($"Failed to load template '{template.Name}': {ex.Message}", CAP_Contracts.Logger.LogLevel.Error, ex);
                    return;
                }

                if (template.TemplateGroup == null)
                {
                    StatusText = $"Template '{template.Name}' could not be loaded - file may be corrupted";
                    return;
                }
            }
            CanvasInteraction.SelectedGroupTemplate = template;
        };

        WireDesignValidation();
        WireHierarchyPanel();
        WireFileOperations();
        WireCommandManager();

        // Initialize panels
        LeftPanel.Initialize();
        RightPanel.Initialize();
    }

    private void WireCommandManager()
    {
        // Wire CommandManager to notify RelayCommands when CanUndo/CanRedo changes
        CommandManager.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(Commands.CommandManager.CanUndo))
            {
                UndoCommand.NotifyCanExecuteChanged();
            }
            else if (e.PropertyName == nameof(Commands.CommandManager.CanRedo))
            {
                RedoCommand.NotifyCanExecuteChanged();
            }
        };
    }

    private void UpdateStatusText(string text)
    {
        StatusText = text;
        BottomPanel.SetStatus(text);
    }

    private void WireHierarchyPanel()
    {
        LeftPanel.HierarchyPanel.NavigateToPosition = ViewportControl.NavigateCanvasTo;
        LeftPanel.HierarchyPanel.GetViewportSize = ViewportControl.GetViewportSize;
    }

    private void WireDesignValidation()
    {
        RightPanel.DesignValidation.NavigateToPosition = ViewportControl.NavigateCanvasTo;
        RightPanel.DesignValidation.HighlightConnection = (connection) =>
        {
            foreach (var conn in Canvas.Connections)
            {
                conn.IsSelected = conn.Connection == connection;
            }
        };
    }

    private void WireFileOperations()
    {
        FileOperations.RebuildHierarchy = LeftPanel.HierarchyPanel.RebuildTree;
        FileOperations.ZoomToFitAfterLoad = (w, h) =>
        {
            var (vpWidth, vpHeight) = ViewportControl.GetViewportSize?.Invoke() ?? (w, h);
            ViewportControl.ZoomToFit(vpWidth, vpHeight);
        };

        // Auto-check Python/Nazca environment on startup
        // If no custom path is set, trigger auto-discovery
        var gdsExport = FileOperations.GdsExport;
        if (string.IsNullOrEmpty(gdsExport.CustomPythonPath))
        {
            _ = gdsExport.SearchForPythonAsync();
        }
        else
        {
            _ = gdsExport.CheckEnvironmentAsync();
        }
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
    private async Task NewProject() => await FileOperations.NewProjectCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task ExportNazca() => await FileOperations.ExportNazcaCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task LoadPdk() => await LeftPanel.LoadPdkCommand.ExecuteAsync(null);

    [RelayCommand]
    private void OpenPdkHelp()
    {
        var helpFile = System.IO.Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "PDK_JSON_FORMAT.md");
        var absolutePath = System.IO.Path.GetFullPath(helpFile);

        if (!System.IO.File.Exists(absolutePath))
        {
            StatusText = "Help file not found. See docs/PDK_JSON_FORMAT.md in the repository.";
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = absolutePath,
            UseShellExecute = true
        });
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
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

    private bool CanUndo() => CommandManager.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
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

    private bool CanRedo() => CommandManager.CanRedo;

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
                    RightPanel.SMatrixPerformance.AnalyzeMatrix(result.SystemMatrix);
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
            BottomPanel.ErrorConsole.Log($"Simulation failed: {ex.Message}", CAP_Contracts.Logger.LogLevel.Error, ex);

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

        var groups = Canvas.Components
            .Select(c => c.Component)
            .OfType<CAP_Core.Components.Core.ComponentGroup>()
            .ToList();

        RightPanel.DesignValidation.RunValidation(connections, groups);
        StatusText = RightPanel.DesignValidation.StatusText;
    }
}

// Data classes for serialization (used by FileOperationsViewModel)
public class DesignFileData
{
    public List<ComponentData> Components { get; set; } = new();
    public List<ConnectionData> Connections { get; set; } = new();

    /// <summary>
    /// ComponentGroups with their hierarchical structure, frozen paths, and external pins.
    /// </summary>
    public List<DesignGroupData>? Groups { get; set; }
}

/// <summary>
/// DTO for a ComponentGroup in the design file.
/// Bridges the UI-layer (TemplateName-based) and core-layer (ComponentGroupDto) serialization.
/// </summary>
public class DesignGroupData
{
    /// <summary>
    /// Group metadata serialized via ComponentGroupSerializer.
    /// </summary>
    public CAP_DataAccess.Persistence.DTOs.ComponentGroupDto GroupDto { get; set; } = new();

    /// <summary>
    /// Child component data with template names for recreation from the component library.
    /// Maps child Identifier to TemplateName.
    /// </summary>
    public List<ChildComponentData> ChildComponents { get; set; } = new();

    /// <summary>
    /// Canvas X position of the group ViewModel.
    /// </summary>
    public double CanvasX { get; set; }

    /// <summary>
    /// Canvas Y position of the group ViewModel.
    /// </summary>
    public double CanvasY { get; set; }
}

/// <summary>
/// DTO for a child component within a group, preserving template name for library lookup.
/// </summary>
public class ChildComponentData
{
    public string Identifier { get; set; } = "";

    /// <summary>
    /// Guid string of the component instance (stable unique ID).
    /// Used as the primary lookup key during load; falls back to Identifier for old files.
    /// </summary>
    public string? ComponentGuid { get; set; }

    public string TemplateName { get; set; } = "";

    /// <summary>
    /// PDK source name used to disambiguate templates with the same name.
    /// Null in old files — falls back to name-only lookup.
    /// </summary>
    public string? PdkSource { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public int Rotation { get; set; }
    public double? SliderValue { get; set; }
    public int? LaserWavelengthNm { get; set; }
    public double? LaserPower { get; set; }
    public bool? IsLocked { get; set; }
    public string? HumanReadableName { get; set; }
}

public class ComponentData
{
    public string TemplateName { get; set; } = "";

    /// <summary>
    /// PDK source name (e.g. "Built-in", "Demo PDK").
    /// Used to disambiguate templates with the same name from different PDKs.
    /// Null in old files — falls back to name-only lookup.
    /// </summary>
    public string? PdkSource { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public string Identifier { get; set; } = "";
    public int Rotation { get; set; }
    public double? SliderValue { get; set; }
    public int? LaserWavelengthNm { get; set; }
    public double? LaserPower { get; set; }
    public bool? IsLocked { get; set; }
    public string? HumanReadableName { get; set; }
}

public class ConnectionData
{
    public int StartComponentIndex { get; set; }
    public string StartPinName { get; set; } = "";
    public int EndComponentIndex { get; set; }
    public string EndPinName { get; set; } = "";

    /// <summary>
    /// Stable component identifier for the start endpoint (preferred over StartComponentIndex).
    /// Populated in new saves; null in old files (fall back to StartComponentIndex).
    /// </summary>
    public string? StartComponentId { get; set; }

    /// <summary>
    /// Stable component identifier for the end endpoint (preferred over EndComponentIndex).
    /// Populated in new saves; null in old files (fall back to EndComponentIndex).
    /// </summary>
    public string? EndComponentId { get; set; }

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
