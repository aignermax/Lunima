using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Components;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;

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
    private string _statusText = "Ready";

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    [ObservableProperty]
    private InteractionMode _currentMode = InteractionMode.Select;

    [ObservableProperty]
    private ComponentTemplate? _selectedTemplate;

    [ObservableProperty]
    private ComponentViewModel? _selectedComponent;

    public ObservableCollection<ComponentTemplate> ComponentLibrary { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();

    public Commands.CommandManager CommandManager { get; } = new();

    public IFileDialogService? FileDialogService { get; set; }

    private PhysicalPin? _connectionStartPin;
    private string? _currentFilePath;

    // For tracking move operations
    private double _moveStartX;
    private double _moveStartY;
    private ComponentViewModel? _movingComponent;

    public MainViewModel()
    {
        _canvas = new DesignCanvasViewModel();
        LoadComponentLibrary();
    }

    private void LoadComponentLibrary()
    {
        var templates = ComponentTemplates.GetAllTemplates();
        var categories = templates.Select(t => t.Category).Distinct().OrderBy(c => c);

        foreach (var category in categories)
        {
            Categories.Add(category);
        }

        foreach (var template in templates)
        {
            ComponentLibrary.Add(template);
        }

        StatusText = $"Loaded {ComponentLibrary.Count} component types";
    }

    partial void OnSelectedTemplateChanged(ComponentTemplate? value)
    {
        if (value != null)
        {
            CurrentMode = InteractionMode.PlaceComponent;
            StatusText = $"Click on canvas to place: {value.Name}";
        }
    }

    partial void OnCurrentModeChanged(InteractionMode value)
    {
        StatusText = value switch
        {
            InteractionMode.Select => "Select mode: Click to select, drag to move",
            InteractionMode.PlaceComponent when SelectedTemplate != null => $"Place mode: Click to place {SelectedTemplate.Name}",
            InteractionMode.PlaceComponent => "Place mode: Select a component from the library",
            InteractionMode.Connect => "Connect mode: Click on a pin to start, then click another pin to connect",
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

    private void HandlePinClickForConnection(PhysicalPin pin)
    {
        if (_connectionStartPin == null)
        {
            _connectionStartPin = pin;
            StatusText = $"Connection started from {pin.Name}. Click another pin to complete.";
        }
        else
        {
            if (_connectionStartPin != pin && _connectionStartPin.ParentComponent != pin.ParentComponent)
            {
                // Create connection via command
                var cmd = new CreateConnectionCommand(Canvas, _connectionStartPin, pin);
                CommandManager.ExecuteCommand(cmd);
                StatusText = $"Connected {_connectionStartPin.Name} to {pin.Name}";
            }
            else
            {
                StatusText = "Cannot connect pin to itself or same component";
            }
            _connectionStartPin = null;
        }
    }

    private void PlaceComponentAt(double x, double y)
    {
        if (SelectedTemplate == null) return;

        var cmd = new PlaceComponentCommand(Canvas, SelectedTemplate, x, y);
        CommandManager.ExecuteCommand(cmd);

        StatusText = $"Placed {SelectedTemplate.Name} at ({x:F0}, {y:F0})µm";
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
            SelectedComponent = component;
            Canvas.SelectedComponent = component;
            StatusText = $"Selected: {component.Name}";
        }
        else
        {
            SelectedComponent = null;
            Canvas.SelectedComponent = null;
        }
    }

    private void DeleteAt(double x, double y)
    {
        // First check for component at position
        var component = Canvas.Components
            .Where(c => x >= c.X && x <= c.X + c.Width && y >= c.Y && y <= c.Y + c.Height)
            .LastOrDefault();

        if (component != null)
        {
            var name = component.Name;
            var cmd = new DeleteComponentCommand(Canvas, component);
            CommandManager.ExecuteCommand(cmd);
            SelectedComponent = null;
            StatusText = $"Deleted: {name}";
            return;
        }

        // Check for connection at position
        var connection = FindConnectionAt(x, y);
        if (connection != null)
        {
            var cmd = new DeleteConnectionCommand(Canvas, connection);
            CommandManager.ExecuteCommand(cmd);
            StatusText = "Deleted connection";
        }
    }

    private WaveguideConnectionViewModel? FindConnectionAt(double x, double y)
    {
        const double hitTolerance = 10.0; // micrometers

        foreach (var conn in Canvas.Connections)
        {
            // Simple line-to-point distance check
            var startX = conn.StartX;
            var startY = conn.StartY;
            var endX = conn.EndX;
            var endY = conn.EndY;

            var distance = PointToLineDistance(x, y, startX, startY, endX, endY);
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
            // Start and end are same point
            return Math.Sqrt((px - x1) * (px - x1) + (py - y1) * (py - y1));
        }

        // Project point onto line segment
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
    }

    /// <summary>
    /// Called when finished dragging a component.
    /// </summary>
    public void EndMoveComponent()
    {
        if (_movingComponent != null &&
            (Math.Abs(_movingComponent.X - _moveStartX) > 0.001 ||
             Math.Abs(_movingComponent.Y - _moveStartY) > 0.001))
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
        _movingComponent = null;
    }

    [RelayCommand]
    private void SetSelectMode()
    {
        CurrentMode = InteractionMode.Select;
        SelectedTemplate = null;
        _connectionStartPin = null;
    }

    [RelayCommand]
    private void SetConnectMode()
    {
        CurrentMode = InteractionMode.Connect;
        SelectedTemplate = null;
        _connectionStartPin = null;
        StatusText = "Connect mode: Click on a pin to start connection";
    }

    [RelayCommand]
    private void SetDeleteMode()
    {
        CurrentMode = InteractionMode.Delete;
        SelectedTemplate = null;
        _connectionStartPin = null;
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedComponent != null)
        {
            var name = SelectedComponent.Name;
            var cmd = new DeleteComponentCommand(Canvas, SelectedComponent);
            CommandManager.ExecuteCommand(cmd);
            SelectedComponent = null;
            StatusText = $"Deleted: {name}";
        }
    }

    [RelayCommand]
    private void RotateSelected()
    {
        if (SelectedComponent != null)
        {
            var cmd = new RotateComponentCommand(Canvas, SelectedComponent);
            CommandManager.ExecuteCommand(cmd);
            StatusText = $"Rotated: {SelectedComponent.Name}";
        }
    }

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
    private async Task ExportNazca()
    {
        if (FileDialogService == null)
        {
            StatusText = "Export not available";
            return;
        }

        if (Canvas.Components.Count == 0)
        {
            StatusText = "Nothing to export - add some components first";
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
                var exporter = new SimpleNazcaExporter();
                var nazcaCode = exporter.Export(Canvas);
                await File.WriteAllTextAsync(filePath, nazcaCode);
                StatusText = $"Exported to {Path.GetFileName(filePath)}";
            }
            catch (Exception ex)
            {
                StatusText = $"Export failed: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    private async Task SaveDesign()
    {
        if (FileDialogService == null)
        {
            StatusText = "Save not available";
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
            StatusText = "Save not available";
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
                    Rotation = (int)c.Component.Rotation90CounterClock
                }).ToList(),
                Connections = Canvas.Connections.Select(c => new ConnectionData
                {
                    StartComponentIndex = Canvas.Components.ToList().FindIndex(
                        comp => comp.Component == c.Connection.StartPin.ParentComponent),
                    StartPinName = c.Connection.StartPin.Name,
                    EndComponentIndex = Canvas.Components.ToList().FindIndex(
                        comp => comp.Component == c.Connection.EndPin.ParentComponent),
                    EndPinName = c.Connection.EndPin.Name
                }).ToList()
            };

            var json = JsonSerializer.Serialize(designData, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
            _currentFilePath = filePath;
            StatusText = $"Saved to {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadDesign()
    {
        if (FileDialogService == null)
        {
            StatusText = "Load not available";
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
                    StatusText = "Invalid design file";
                    return;
                }

                // Clear current design
                Canvas.Components.Clear();
                Canvas.Connections.Clear();
                Canvas.ConnectionManager.Clear();
                CommandManager.ClearHistory();

                // Load components
                var templates = ComponentTemplates.GetAllTemplates();
                foreach (var compData in designData.Components)
                {
                    var template = templates.FirstOrDefault(t =>
                        t.Name.Equals(compData.TemplateName, StringComparison.OrdinalIgnoreCase));

                    if (template != null)
                    {
                        var component = ComponentTemplates.CreateFromTemplate(template, compData.X, compData.Y);

                        // Apply rotation
                        for (int i = 0; i < compData.Rotation; i++)
                        {
                            ApplyRotationToComponent(component);
                        }

                        Canvas.AddComponent(component, template.Name);
                    }
                }

                // Load connections
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
                            Canvas.ConnectPins(startPin, endPin);
                        }
                    }
                }

                _currentFilePath = filePath;
                StatusText = $"Loaded {Path.GetFileName(filePath)} ({Canvas.Components.Count} components, {Canvas.Connections.Count} connections)";
                CommandManager.NotifyStateChanged();
            }
            catch (Exception ex)
            {
                StatusText = $"Load failed: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// Applies a 90° counter-clockwise rotation to a component (same logic as RotateComponentCommand).
    /// </summary>
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
            pin.AngleDegrees = (pin.AngleDegrees + 90) % 360;
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
    public int Rotation { get; set; } // 0, 1, 2, 3 for R0, R90, R180, R270
}

public class ConnectionData
{
    public int StartComponentIndex { get; set; }
    public string StartPinName { get; set; } = "";
    public int EndComponentIndex { get; set; }
    public string EndPinName { get; set; } = "";
}
