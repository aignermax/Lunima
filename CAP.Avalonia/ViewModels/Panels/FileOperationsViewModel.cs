using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_DataAccess.Persistence;
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

    [ObservableProperty]
    private bool _hasUnsavedChanges;

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

    /// <summary>
    /// Message box service for showing confirmation dialogs.
    /// </summary>
    public IMessageBoxService? MessageBoxService { get; set; }

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

        // Track changes to mark project as unsaved
        _canvas.Components.CollectionChanged += (s, e) => HasUnsavedChanges = true;
        _canvas.Connections.CollectionChanged += (s, e) => HasUnsavedChanges = true;
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
            // Identify which components are groups vs standalone
            var groupComponents = _canvas.Components
                .Where(c => c.Component is ComponentGroup)
                .ToList();
            var childComponentIds = new HashSet<string>();
            foreach (var gc in groupComponents)
            {
                CollectChildIds((ComponentGroup)gc.Component, childComponentIds);
            }

            var designData = new DesignFileData
            {
                // Only save non-group, non-child components in the main list
                Components = _canvas.Components
                    .Where(c => c.Component is not ComponentGroup
                                && !childComponentIds.Contains(c.Component.Identifier))
                    .Select(c => CreateComponentData(c))
                    .ToList(),
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

            // Serialize groups
            if (groupComponents.Count > 0)
            {
                designData.Groups = groupComponents.Select(gc =>
                    SerializeGroupToDesignData(gc)).ToList();
            }

            var json = JsonSerializer.Serialize(designData, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            await File.WriteAllTextAsync(filePath, json);
            _currentFilePath = filePath;
            HasUnsavedChanges = false;
            UpdateStatus?.Invoke($"Saved to {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            UpdateStatus?.Invoke($"Save failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a ComponentData DTO from a ComponentViewModel.
    /// </summary>
    private static ComponentData CreateComponentData(ComponentViewModel c)
    {
        return new ComponentData
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
        };
    }

    /// <summary>
    /// Serializes a ComponentGroup ViewModel into a DesignGroupData for the design file.
    /// </summary>
    private DesignGroupData SerializeGroupToDesignData(ComponentViewModel groupVm)
    {
        var group = (ComponentGroup)groupVm.Component;
        var groupDto = ComponentGroupSerializer.ToDto(group);

        var childDataList = new List<ChildComponentData>();
        CollectChildComponentData(group, childDataList);

        return new DesignGroupData
        {
            GroupDto = groupDto,
            ChildComponents = childDataList,
            CanvasX = groupVm.X,
            CanvasY = groupVm.Y
        };
    }

    /// <summary>
    /// Recursively collects child component data (with template names) from a group.
    /// </summary>
    private void CollectChildComponentData(
        ComponentGroup group, List<ChildComponentData> childDataList)
    {
        foreach (var child in group.ChildComponents)
        {
            if (child is ComponentGroup childGroup)
            {
                // Nested groups are handled by their own DesignGroupData
                CollectChildComponentData(childGroup, childDataList);
            }
            else
            {
                var templateName = FindTemplateName(child);

                childDataList.Add(new ChildComponentData
                {
                    Identifier = child.Identifier,
                    TemplateName = templateName,
                    X = child.PhysicalX,
                    Y = child.PhysicalY,
                    Rotation = (int)child.Rotation90CounterClock,
                    SliderValue = child.GetAllSliders().Count > 0
                        ? child.GetSlider(0)?.Value : null,
                    IsLocked = child.IsLocked ? true : null
                });
            }
        }
    }

    /// <summary>
    /// Finds the template name for a component by checking the canvas VMs
    /// and falling back to matching against the component library by NazcaFunctionName.
    /// </summary>
    private string FindTemplateName(Component component)
    {
        // Check if the component has a VM on the canvas with a template name
        var vm = _canvas.Components.FirstOrDefault(c => c.Component == component);
        if (vm?.TemplateName != null)
            return vm.TemplateName;

        // Match by NazcaFunctionName against the component library
        var nazcaFunc = component.NazcaFunctionName;
        if (!string.IsNullOrEmpty(nazcaFunc))
        {
            var match = _componentLibrary.FirstOrDefault(t =>
            {
                var templateFunc = t.NazcaFunctionName
                    ?? $"nazca_{t.Name.ToLower().Replace(" ", "_")}";
                return templateFunc == nazcaFunc;
            });
            if (match != null)
                return match.Name;
        }

        // Last resort: use identifier
        return component.Identifier;
    }

    /// <summary>
    /// Recursively collects all child component identifiers from a group.
    /// </summary>
    private static void CollectChildIds(ComponentGroup group, HashSet<string> ids)
    {
        foreach (var child in group.ChildComponents)
        {
            ids.Add(child.Identifier);
            if (child is ComponentGroup nested)
            {
                CollectChildIds(nested, ids);
            }
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

                // Load standalone components
                foreach (var compData in designData.Components)
                {
                    LoadComponentFromData(compData);
                }

                // Load ComponentGroups
                var groupCount = 0;
                if (designData.Groups != null)
                {
                    groupCount = LoadGroups(designData.Groups);
                }

                // Load connections (index-based references to _canvas.Components)
                foreach (var connData in designData.Connections)
                {
                    LoadConnectionFromData(connData);
                }

                // Notify all connections about their paths for UI rendering
                foreach (var conn in _canvas.Connections)
                {
                    conn.NotifyPathChanged();
                }

                _currentFilePath = filePath;
                HasUnsavedChanges = false;
                UpdateStatus?.Invoke($"Loaded {Path.GetFileName(filePath)} ({_canvas.Components.Count} components, {_canvas.Connections.Count} connections, {groupCount} groups)");
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

    /// <summary>
    /// Creates a new empty project, prompting to save if there are unsaved changes.
    /// Exits group edit mode if active before clearing the canvas.
    /// </summary>
    [RelayCommand]
    private async Task NewProject()
    {
        // Check if there are unsaved changes
        if (HasUnsavedChanges && MessageBoxService != null)
        {
            var result = await MessageBoxService.ShowSavePromptAsync(
                "Do you want to save your changes before creating a new project?",
                "Save Changes?");

            if (result == SavePromptResult.Save)
            {
                await SaveDesign();

                // Check if save was actually performed (user might have cancelled)
                if (HasUnsavedChanges)
                {
                    // User cancelled the save dialog, so cancel new project
                    return;
                }
            }
            else if (result == SavePromptResult.Cancel)
            {
                // User cancelled, do nothing
                return;
            }
            // DontSave: continue to clear
        }

        // Exit group edit mode if active
        if (_canvas.IsInGroupEditMode)
        {
            _canvas.ExitToRoot();
        }

        // Clear the canvas
        ClearCanvas();

        _currentFilePath = null;
        HasUnsavedChanges = false;
        UpdateStatus?.Invoke("New project created");

        // Rebuild hierarchy
        RebuildHierarchy?.Invoke();
    }

    /// <summary>
    /// Clears all components and connections from the canvas.
    /// </summary>
    private void ClearCanvas()
    {
        _canvas.Components.Clear();
        _canvas.Connections.Clear();
        _canvas.ConnectionManager.Clear();
        _commandManager.ClearHistory();
    }

    /// <summary>
    /// Loads a single component from saved data and adds it to the canvas.
    /// </summary>
    private ComponentViewModel? LoadComponentFromData(ComponentData compData)
    {
        var template = _componentLibrary.FirstOrDefault(t =>
            t.Name.Equals(compData.TemplateName, StringComparison.OrdinalIgnoreCase));

        if (template == null)
            return null;

        var component = ComponentTemplates.CreateFromTemplate(template, compData.X, compData.Y);

        // Restore identifier to preserve references
        component.Identifier = compData.Identifier;

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

        return vm;
    }

    /// <summary>
    /// Loads ComponentGroups from saved design data.
    /// Creates child components, reconstructs groups, and adds them to the canvas.
    /// </summary>
    private int LoadGroups(List<DesignGroupData> groupDataList)
    {
        var loadedCount = 0;

        foreach (var groupData in groupDataList)
        {
            var componentLookup = new Dictionary<string, Component>();

            // Create child components from template (not added to canvas individually)
            foreach (var childData in groupData.ChildComponents)
            {
                var template = _componentLibrary.FirstOrDefault(t =>
                    t.Name.Equals(childData.TemplateName, StringComparison.OrdinalIgnoreCase));

                if (template == null)
                    continue;

                var child = ComponentTemplates.CreateFromTemplate(
                    template, childData.X, childData.Y);

                // Restore original identifier for reference matching
                child.Identifier = childData.Identifier;

                // Apply rotation
                for (int i = 0; i < childData.Rotation; i++)
                {
                    ApplyRotationToComponent(child);
                }

                // Restore slider
                if (childData.SliderValue.HasValue && child.GetAllSliders().Count > 0)
                {
                    var slider = child.GetSlider(0);
                    if (slider != null) slider.Value = childData.SliderValue.Value;
                }

                if (childData.IsLocked == true)
                    child.IsLocked = true;

                componentLookup[child.Identifier] = child;
            }

            // Reconstruct the group from DTO
            var group = ComponentGroupSerializer.FromDto(
                groupData.GroupDto, componentLookup);

            // Add the group to the canvas as a ComponentViewModel
            var groupVm = _canvas.AddComponent(group);
            groupVm.X = groupData.CanvasX;
            groupVm.Y = groupData.CanvasY;
            group.PhysicalX = groupData.CanvasX;
            group.PhysicalY = groupData.CanvasY;

            loadedCount++;
        }

        return loadedCount;
    }

    /// <summary>
    /// Loads a single connection from saved data.
    /// </summary>
    private void LoadConnectionFromData(ConnectionData connData)
    {
        if (connData.StartComponentIndex < 0 ||
            connData.StartComponentIndex >= _canvas.Components.Count ||
            connData.EndComponentIndex < 0 ||
            connData.EndComponentIndex >= _canvas.Components.Count)
        {
            return;
        }

        var startComp = _canvas.Components[connData.StartComponentIndex];
        var endComp = _canvas.Components[connData.EndComponentIndex];

        var startPin = startComp.Component.PhysicalPins
            .FirstOrDefault(p => p.Name == connData.StartPinName);
        var endPin = endComp.Component.PhysicalPins
            .FirstOrDefault(p => p.Name == connData.EndPinName);

        if (startPin == null || endPin == null)
            return;

        var cachedPath = PathSegmentConverter.ToRoutedPath(
            connData.CachedSegments, connData.IsBlockedFallback ?? false);

        WaveguideConnectionViewModel? connVm;

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
