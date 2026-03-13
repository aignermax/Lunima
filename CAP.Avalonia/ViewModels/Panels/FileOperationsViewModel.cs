using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Converters;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Export;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// ViewModel for file operations (save, load, export).
/// Handles all design file I/O and export functionality.
/// Max 250 lines per CLAUDE.md guideline.
/// </summary>
public partial class FileOperationsViewModel : ObservableObject
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly CommandManager _commandManager;
    private readonly SimpleNazcaExporter _nazcaExporter;
    private readonly ObservableCollection<ComponentTemplate> _componentLibrary;

    private string? _currentFilePath;

    /// <summary>
    /// ViewModel for GDS export functionality.
    /// </summary>
    public GdsExportViewModel GdsExport { get; }

    /// <summary>
    /// Callback to update status text in the UI.
    /// </summary>
    public Action<string>? UpdateStatus { get; set; }

    /// <summary>
    /// Callback to rebuild hierarchy tree after loading.
    /// </summary>
    public Action? RebuildHierarchy { get; set; }

    /// <summary>
    /// Callback to trigger zoom-to-fit after loading.
    /// </summary>
    public Action<double, double>? ZoomToFitAfterLoad { get; set; }

    /// <summary>
    /// File dialog service for showing open/save dialogs.
    /// </summary>
    public IFileDialogService? FileDialogService { get; set; }

    public FileOperationsViewModel(
        DesignCanvasViewModel canvas,
        CommandManager commandManager,
        SimpleNazcaExporter nazcaExporter,
        ObservableCollection<ComponentTemplate> componentLibrary,
        GdsExportViewModel gdsExport)
    {
        _canvas = canvas;
        _commandManager = commandManager;
        _nazcaExporter = nazcaExporter;
        _componentLibrary = componentLibrary;
        GdsExport = gdsExport;
    }

    [RelayCommand]
    private async Task SaveDesign()
    {
        if (FileDialogService == null)
        {
            UpdateStatus?.Invoke("Save not available");
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
            UpdateStatus?.Invoke("Save not available");
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
                Components = _canvas.Components.Select(c => new ComponentData
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
                Connections = _canvas.Connections.Select(c => new ConnectionData
                {
                    StartComponentIndex = _canvas.Components.ToList().FindIndex(
                        comp => comp.Component == c.Connection.StartPin.ParentComponent),
                    StartPinName = c.Connection.StartPin.Name,
                    EndComponentIndex = _canvas.Components.ToList().FindIndex(
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
            UpdateStatus?.Invoke($"Saved to {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            UpdateStatus?.Invoke($"Save failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task LoadDesign()
    {
        if (FileDialogService == null)
        {
            UpdateStatus?.Invoke("Load not available");
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
                    UpdateStatus?.Invoke("Invalid design file");
                    return;
                }

                // Clear current design
                _canvas.Components.Clear();
                _canvas.Connections.Clear();
                _canvas.ConnectionManager.Clear();
                _commandManager.ClearHistory();

                // Load components
                foreach (var compData in designData.Components)
                {
                    var template = _componentLibrary.FirstOrDefault(t =>
                        t.Name.Equals(compData.TemplateName, StringComparison.OrdinalIgnoreCase));

                    if (template != null)
                    {
                        var component = ComponentTemplates.CreateFromTemplate(template, compData.X, compData.Y);

                        // Apply rotation
                        for (int i = 0; i < compData.Rotation; i++)
                        {
                            ApplyRotationToComponent(component);
                        }

                        var vm = _canvas.AddComponent(component, template.Name);

                        // Restore slider value
                        if (compData.SliderValue.HasValue && vm.HasSliders)
                            vm.SliderValue = compData.SliderValue.Value;

                        // Restore laser configuration
                        if (vm.LaserConfig != null)
                        {
                            if (compData.LaserWavelengthNm.HasValue)
                                vm.LaserConfig.WavelengthNm = compData.LaserWavelengthNm.Value;
                            if (compData.LaserPower.HasValue)
                                vm.LaserConfig.InputPower = compData.LaserPower.Value;
                        }

                        // Restore lock state
                        if (compData.IsLocked == true)
                            component.IsLocked = true;
                    }
                }

                // Load connections
                foreach (var connData in designData.Connections)
                {
                    if (connData.StartComponentIndex >= 0 && connData.StartComponentIndex < _canvas.Components.Count &&
                        connData.EndComponentIndex >= 0 && connData.EndComponentIndex < _canvas.Components.Count)
                    {
                        var startComp = _canvas.Components[connData.StartComponentIndex];
                        var endComp = _canvas.Components[connData.EndComponentIndex];

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
                                connVm = _canvas.ConnectPinsWithCachedRoute(startPin, endPin, cachedPath);
                            }
                            else
                            {
                                connVm = _canvas.ConnectPins(startPin, endPin);
                            }

                            // Restore lock state
                            if (connVm != null && connData.IsLocked == true)
                            {
                                connVm.Connection.IsLocked = true;
                            }

                            // Restore target length configuration
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

                // Notify all connections about their paths for UI rendering
                foreach (var conn in _canvas.Connections)
                {
                    conn.NotifyPathChanged();
                }

                _currentFilePath = filePath;
                UpdateStatus?.Invoke($"Loaded {Path.GetFileName(filePath)} ({_canvas.Components.Count} components, {_canvas.Connections.Count} connections)");
                _commandManager.NotifyStateChanged();

                // Rebuild hierarchy tree after loading
                RebuildHierarchy?.Invoke();

                // Auto zoom-to-fit after loading
                ZoomToFitAfterLoad?.Invoke(900, 800);
            }
            catch (Exception ex)
            {
                UpdateStatus?.Invoke($"Load failed: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private async Task ExportNazca()
    {
        if (FileDialogService == null)
        {
            UpdateStatus?.Invoke("Export not available");
            return;
        }

        if (_canvas.Components.Count == 0)
        {
            UpdateStatus?.Invoke("Nothing to export - add some components first");
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
                // Export Python script
                var nazcaCode = _nazcaExporter.Export(_canvas);
                await File.WriteAllTextAsync(filePath, nazcaCode);

                // Attempt GDS generation if enabled
                var result = await GdsExport.ExportScriptToGdsAsync(filePath);

                if (result.Success && result.GdsPath != null)
                {
                    UpdateStatus?.Invoke($"Exported {Path.GetFileName(filePath)} and {Path.GetFileName(result.GdsPath)}");
                }
                else if (result.Success)
                {
                    UpdateStatus?.Invoke($"Exported to {Path.GetFileName(filePath)}");
                }
                else
                {
                    UpdateStatus?.Invoke($"Exported {Path.GetFileName(filePath)} (GDS generation failed: {result.ErrorMessage})");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus?.Invoke($"Export failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Applies a 90° counter-clockwise rotation to a component.
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
        }

        comp.WidthMicrometers = height;
        comp.HeightMicrometers = width;
        comp.RotateBy90CounterClockwise();
    }
}
