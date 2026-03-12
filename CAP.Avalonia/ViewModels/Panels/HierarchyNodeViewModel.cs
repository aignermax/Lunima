using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// Represents a single node in the hierarchy tree (either a component or a group).
/// </summary>
public partial class HierarchyNodeViewModel : ObservableObject
{
    /// <summary>
    /// The component this node represents.
    /// </summary>
    public ComponentViewModel Component { get; }

    /// <summary>
    /// Display name for this node.
    /// For groups: "GroupName (X components)", for components: component name.
    /// </summary>
    [ObservableProperty]
    private string _displayName;

    /// <summary>
    /// Icon glyph for this node.
    /// Folder icon for groups, box icon for components.
    /// </summary>
    [ObservableProperty]
    private string _iconGlyph;

    /// <summary>
    /// Whether this node is expanded (for groups).
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Whether this node is currently selected in the hierarchy.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Child nodes (for groups).
    /// </summary>
    public ObservableCollection<HierarchyNodeViewModel> Children { get; } = new();

    /// <summary>
    /// Whether this node is a group (has children).
    /// </summary>
    public bool IsGroup => Children.Count > 0;

    /// <summary>
    /// Callback to invoke when this node needs to be focused (zoom to component).
    /// </summary>
    public Action<ComponentViewModel>? OnFocusRequested { get; set; }

    public HierarchyNodeViewModel(ComponentViewModel component)
    {
        Component = component;
        _displayName = component.Name;
        _iconGlyph = "📦"; // Default component icon

        // Subscribe to component name changes
        component.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ComponentViewModel.Name))
            {
                UpdateDisplayName();
            }
        };
    }

    /// <summary>
    /// Toggles the expanded state of this node.
    /// </summary>
    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }

    /// <summary>
    /// Requests focus on this component (zoom canvas to this component).
    /// </summary>
    [RelayCommand]
    private void FocusComponent()
    {
        OnFocusRequested?.Invoke(Component);
    }

    /// <summary>
    /// Updates the display name based on the component type and children.
    /// </summary>
    public void UpdateDisplayName()
    {
        if (IsGroup)
        {
            DisplayName = $"{Component.Name} ({Children.Count} component{(Children.Count == 1 ? "" : "s")})";
            IconGlyph = IsExpanded ? "📂" : "📁"; // Open/closed folder
        }
        else
        {
            DisplayName = Component.Name;
            IconGlyph = "📦"; // Component box
        }
    }

    /// <summary>
    /// Adds a child node to this group.
    /// </summary>
    public void AddChild(HierarchyNodeViewModel child)
    {
        Children.Add(child);
        UpdateDisplayName();
    }

    /// <summary>
    /// Removes a child node from this group.
    /// </summary>
    public void RemoveChild(HierarchyNodeViewModel child)
    {
        Children.Remove(child);
        UpdateDisplayName();
    }

    partial void OnIsExpandedChanged(bool value)
    {
        UpdateDisplayName();
    }
}
