using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Components.Core;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Diagnostics;

/// <summary>
/// ViewModel for the Component Group UI panel.
/// Allows users to create hierarchical groups from selected components.
/// </summary>
public partial class ComponentGroupViewModel : ObservableObject
{
    [ObservableProperty]
    private string _groupName = "Unnamed Group";

    [ObservableProperty]
    private string _groupDescription = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _canCreateGroup;

    [ObservableProperty]
    private int _selectedComponentCount;

    [ObservableProperty]
    private string _groupInfoText = "No group selected";

    private DesignCanvasViewModel? _canvas;
    private ComponentGroup? _currentGroup;

    /// <summary>
    /// Configures the panel with the current canvas state.
    /// </summary>
    public void ConfigureForCanvas(DesignCanvasViewModel? canvas)
    {
        _canvas = canvas;
        UpdateCanCreateGroup();
    }

    /// <summary>
    /// Updates the "can create group" state based on selected components.
    /// </summary>
    public void UpdateCanCreateGroup()
    {
        if (_canvas == null)
        {
            CanCreateGroup = false;
            SelectedComponentCount = 0;
            return;
        }

        var selected = _canvas.Components.Where(c => c.IsSelected).ToList();
        SelectedComponentCount = selected.Count;
        CanCreateGroup = selected.Count >= 2;

        if (CanCreateGroup)
        {
            StatusText = $"{selected.Count} components selected - ready to group";
        }
        else if (selected.Count == 1)
        {
            StatusText = "Select at least 2 components to create a group";
        }
        else
        {
            StatusText = "Select components to create a group";
        }
    }

    /// <summary>
    /// Creates a group from the currently selected components.
    /// </summary>
    [RelayCommand]
    private void CreateGroup()
    {
        if (_canvas == null || !CanCreateGroup) return;

        try
        {
            var selected = _canvas.Components.Where(c => c.IsSelected).ToList();
            if (selected.Count < 2)
            {
                StatusText = "Need at least 2 components to create a group";
                return;
            }

            // Create the group
            var group = new ComponentGroup(GroupName)
            {
                Description = GroupDescription
            };

            // Calculate group origin (top-left of bounding box)
            double minX = selected.Min(c => c.Component.PhysicalX);
            double minY = selected.Min(c => c.Component.PhysicalY);
            group.PhysicalX = minX;
            group.PhysicalY = minY;

            // Add all selected components as children
            foreach (var compVm in selected)
            {
                group.AddChild(compVm.Component);
            }

            // Collect internal waveguide connections
            var internalConnections = _canvas.Connections
                .Where(conn => IsInternalConnection(conn, selected))
                .ToList();

            // Convert internal connections to frozen paths
            foreach (var connVm in internalConnections)
            {
                var frozenPath = CreateFrozenPath(connVm);
                if (frozenPath != null)
                {
                    group.AddInternalPath(frozenPath);
                }
            }

            _currentGroup = group;
            StatusText = $"Created group '{GroupName}' with {selected.Count} components";
            UpdateGroupInfo(group);

            // Reset name for next group
            GroupName = "Unnamed Group";
            GroupDescription = "";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to create group: {ex.Message}";
        }
    }

    /// <summary>
    /// Clears the current group selection.
    /// </summary>
    [RelayCommand]
    private void ClearGroup()
    {
        _currentGroup = null;
        GroupInfoText = "No group selected";
        StatusText = "";
    }

    /// <summary>
    /// Checks if a connection is internal to the selected components.
    /// </summary>
    private bool IsInternalConnection(WaveguideConnectionViewModel conn, List<ComponentViewModel> selected)
    {
        var selectedComponents = selected.Select(c => c.Component).ToHashSet();

        // Check if both pins belong to components in the selection
        var startComp = conn.Connection?.StartPin?.ParentComponent;
        var endComp = conn.Connection?.EndPin?.ParentComponent;

        return startComp != null && endComp != null &&
               selectedComponents.Contains(startComp) &&
               selectedComponents.Contains(endComp);
    }

    /// <summary>
    /// Converts a WaveguideConnectionViewModel to a FrozenWaveguidePath.
    /// </summary>
    private FrozenWaveguidePath? CreateFrozenPath(WaveguideConnectionViewModel connVm)
    {
        if (connVm.Connection == null || connVm.Connection.RoutedPath == null) return null;

        return new FrozenWaveguidePath
        {
            Path = connVm.Connection.RoutedPath,
            StartPin = connVm.Connection.StartPin,
            EndPin = connVm.Connection.EndPin
        };
    }

    /// <summary>
    /// Updates the group info display.
    /// </summary>
    private void UpdateGroupInfo(ComponentGroup group)
    {
        var info = $"Group: {group.GroupName}\n" +
                   $"Children: {group.ChildComponents.Count}\n" +
                   $"Internal Paths: {group.InternalPaths.Count}\n" +
                   $"Size: {group.WidthMicrometers:F1} × {group.HeightMicrometers:F1} µm";

        if (!string.IsNullOrWhiteSpace(group.Description))
        {
            info += $"\n\nDescription:\n{group.Description}";
        }

        GroupInfoText = info;
    }

    /// <summary>
    /// Tests moving a group (demonstrates translation without re-routing).
    /// </summary>
    [RelayCommand]
    private void TestMoveGroup()
    {
        if (_currentGroup == null)
        {
            StatusText = "No group to move";
            return;
        }

        try
        {
            const double testDelta = 100.0; // Move 100 µm right
            _currentGroup.MoveGroup(testDelta, 0);
            StatusText = $"Moved group by {testDelta} µm (frozen paths translated)";
            UpdateGroupInfo(_currentGroup);
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to move group: {ex.Message}";
        }
    }

    /// <summary>
    /// Tests rotating a group (demonstrates 90° rotation).
    /// </summary>
    [RelayCommand]
    private void TestRotateGroup()
    {
        if (_currentGroup == null)
        {
            StatusText = "No group to rotate";
            return;
        }

        try
        {
            _currentGroup.RotateGroupBy90CounterClockwise();
            StatusText = "Rotated group by 90° counter-clockwise";
            UpdateGroupInfo(_currentGroup);
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to rotate group: {ex.Message}";
        }
    }
}
