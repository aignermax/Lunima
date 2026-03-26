using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Components.Core;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Hierarchy;

/// <summary>
/// Represents a single node in the hierarchy tree (component or group).
/// Used in the Figma-style hierarchy panel for visualizing component structure.
/// </summary>
public partial class HierarchyNodeViewModel : ObservableObject
{
    /// <summary>
    /// The underlying component (Component or ComponentGroup).
    /// </summary>
    public Component Component { get; }

    /// <summary>
    /// Reference to the ComponentViewModel wrapper (for selection and navigation).
    /// </summary>
    public ComponentViewModel? ComponentViewModel { get; set; }

    /// <summary>
    /// Child nodes (for ComponentGroups only, empty for regular components).
    /// </summary>
    public ObservableCollection<HierarchyNodeViewModel> Children { get; } = new();

    /// <summary>
    /// Whether this node is expanded in the tree view (only applies to groups).
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>
    /// Whether this node is currently selected (synchronized with canvas selection).
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Whether this group is currently being edited (entered edit mode).
    /// </summary>
    [ObservableProperty]
    private bool _isInEditMode;

    /// <summary>
    /// Whether this component is a group (has children).
    /// </summary>
    public bool IsGroup => Component is ComponentGroup;

    /// <summary>
    /// Icon glyph for display (folder for groups, box for components).
    /// </summary>
    public string IconGlyph => IsGroup
        ? (IsExpanded ? "📂" : "📁") // Open/closed folder
        : "🔷"; // Regular component

    /// <summary>
    /// Display name with child count for groups.
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (Component is ComponentGroup group)
            {
                int childCount = group.ChildComponents.Count;
                return $"{group.GroupName} ({childCount})";
            }
            return Component.HumanReadableName ?? Component.Identifier;
        }
    }

    /// <summary>
    /// Component type label (e.g., "Waveguide", "Coupler", "Group").
    /// </summary>
    public string TypeLabel => Component is ComponentGroup ? "Group" : Component.NazcaFunctionName ?? "Component";

    /// <summary>
    /// Callback to focus/zoom the canvas to this component's position.
    /// </summary>
    public Action<HierarchyNodeViewModel>? FocusRequested { get; set; }

    /// <summary>
    /// Callback to select this component on the canvas.
    /// </summary>
    public Action<HierarchyNodeViewModel>? SelectionRequested { get; set; }

    public HierarchyNodeViewModel(Component component)
    {
        Component = component ?? throw new ArgumentNullException(nameof(component));
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(IsExpanded))
            {
                OnPropertyChanged(nameof(IconGlyph));
            }
        };

        // Subscribe to ComponentGroup property changes for real-time updates
        if (component is ComponentGroup group)
        {
            group.PropertyChanged += OnComponentGroupPropertyChanged;
        }
    }

    /// <summary>
    /// Handles property changes from the underlying ComponentGroup.
    /// Updates the display when GroupName or child count changes.
    /// </summary>
    private void OnComponentGroupPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ComponentGroup.GroupName))
        {
            RefreshDisplayName();
        }
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
    /// Focuses the canvas on this component (zoom to view).
    /// </summary>
    [RelayCommand]
    private void Focus()
    {
        FocusRequested?.Invoke(this);
    }

    /// <summary>
    /// Selects this component on the canvas.
    /// </summary>
    [RelayCommand]
    private void Select()
    {
        SelectionRequested?.Invoke(this);
    }

    /// <summary>
    /// Recursively finds a node by its component reference.
    /// </summary>
    public HierarchyNodeViewModel? FindNodeByComponent(Component component)
    {
        if (Component == component)
            return this;

        foreach (var child in Children)
        {
            var found = child.FindNodeByComponent(component);
            if (found != null)
                return found;
        }

        return null;
    }

    /// <summary>
    /// Expands all parent nodes up to the root (used when selecting a deeply nested component).
    /// </summary>
    public void ExpandToRoot()
    {
        IsExpanded = true;
        // Parent tracking would be needed for upward traversal - for now, caller handles this
    }

    /// <summary>
    /// Updates the display name (e.g., when child count changes).
    /// </summary>
    public void RefreshDisplayName()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IconGlyph));
    }
}
