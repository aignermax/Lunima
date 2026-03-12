using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// ViewModel for the hierarchy panel showing the component tree structure.
/// Displays components in a Figma-style tree view with expand/collapse controls.
/// </summary>
public partial class HierarchyPanelViewModel : ObservableObject
{
    private DesignCanvasViewModel? _canvas;

    /// <summary>
    /// Root nodes in the hierarchy tree.
    /// </summary>
    public ObservableCollection<HierarchyNodeViewModel> RootNodes { get; } = new();

    /// <summary>
    /// Currently selected node in the hierarchy.
    /// </summary>
    [ObservableProperty]
    private HierarchyNodeViewModel? _selectedNode;

    /// <summary>
    /// Status text showing component count.
    /// </summary>
    [ObservableProperty]
    private string _statusText = "No components";

    /// <summary>
    /// Callback to invoke when a component needs to be focused (zoom to).
    /// Set by MainViewModel.
    /// </summary>
    public Action<double, double>? OnFocusRequested { get; set; }

    /// <summary>
    /// Configures this hierarchy panel with a canvas ViewModel.
    /// </summary>
    public void Configure(DesignCanvasViewModel canvas)
    {
        _canvas = canvas;

        // Listen to component collection changes
        canvas.Components.CollectionChanged += OnComponentsChanged;

        // Initial build
        RebuildTree();
    }

    /// <summary>
    /// Rebuilds the entire hierarchy tree from the canvas components.
    /// </summary>
    private void RebuildTree()
    {
        if (_canvas == null) return;

        RootNodes.Clear();

        // Build flat list first (we don't have nested groups yet, so all components are roots)
        foreach (var component in _canvas.Components)
        {
            var node = new HierarchyNodeViewModel(component);
            node.OnFocusRequested = OnComponentFocusRequested;

            // Wire up selection synchronization
            component.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ComponentViewModel.IsSelected))
                {
                    SynchronizeSelectionFromCanvas(component);
                }
            };

            RootNodes.Add(node);
        }

        UpdateStatusText();
    }

    /// <summary>
    /// Handles changes to the canvas component collection.
    /// </summary>
    private void OnComponentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // For simplicity, rebuild the entire tree on any change
        // In a production system, you'd handle Add/Remove incrementally
        RebuildTree();
    }

    /// <summary>
    /// Synchronizes selection from canvas to hierarchy.
    /// </summary>
    private void SynchronizeSelectionFromCanvas(ComponentViewModel component)
    {
        // Find node for this component
        var node = FindNodeForComponent(component);
        if (node == null) return;

        if (component.IsSelected)
        {
            // Deselect all other nodes
            DeselectAllNodes();

            // Select this node
            node.IsSelected = true;
            SelectedNode = node;

            // Expand parent nodes if needed
            ExpandParentNodes(node);

            // Scroll to selected node (would be handled by view)
        }
        else
        {
            // Deselect this node
            node.IsSelected = false;
            if (SelectedNode == node)
            {
                SelectedNode = null;
            }
        }
    }

    /// <summary>
    /// Synchronizes selection from hierarchy to canvas.
    /// </summary>
    partial void OnSelectedNodeChanged(HierarchyNodeViewModel? oldValue, HierarchyNodeViewModel? newValue)
    {
        if (_canvas == null) return;

        // Deselect old component
        if (oldValue != null)
        {
            oldValue.Component.IsSelected = false;
            oldValue.IsSelected = false;
        }

        // Select new component
        if (newValue != null)
        {
            // Deselect all other components
            foreach (var comp in _canvas.Components)
            {
                comp.IsSelected = false;
            }

            newValue.Component.IsSelected = true;
            newValue.IsSelected = true;

            // Update canvas selection
            _canvas.SelectedComponent = newValue.Component;
        }
    }

    /// <summary>
    /// Handles focus request from a node.
    /// </summary>
    private void OnComponentFocusRequested(ComponentViewModel component)
    {
        // Calculate component center
        double centerX = component.X + component.Width / 2;
        double centerY = component.Y + component.Height / 2;

        OnFocusRequested?.Invoke(centerX, centerY);
    }

    /// <summary>
    /// Finds the node for a given component.
    /// </summary>
    private HierarchyNodeViewModel? FindNodeForComponent(ComponentViewModel component)
    {
        foreach (var node in RootNodes)
        {
            var found = FindNodeRecursive(node, component);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Recursively searches for a node matching the component.
    /// </summary>
    private HierarchyNodeViewModel? FindNodeRecursive(HierarchyNodeViewModel node, ComponentViewModel component)
    {
        if (node.Component == component)
            return node;

        foreach (var child in node.Children)
        {
            var found = FindNodeRecursive(child, component);
            if (found != null) return found;
        }

        return null;
    }

    /// <summary>
    /// Deselects all nodes in the tree.
    /// </summary>
    private void DeselectAllNodes()
    {
        foreach (var node in RootNodes)
        {
            DeselectNodeRecursive(node);
        }
    }

    /// <summary>
    /// Recursively deselects a node and its children.
    /// </summary>
    private void DeselectNodeRecursive(HierarchyNodeViewModel node)
    {
        node.IsSelected = false;
        foreach (var child in node.Children)
        {
            DeselectNodeRecursive(child);
        }
    }

    /// <summary>
    /// Expands parent nodes for the given node.
    /// </summary>
    private void ExpandParentNodes(HierarchyNodeViewModel node)
    {
        // Find parent and expand it
        var parent = FindParentNode(node);
        if (parent != null)
        {
            parent.IsExpanded = true;
            ExpandParentNodes(parent);
        }
    }

    /// <summary>
    /// Finds the parent node of the given node.
    /// </summary>
    private HierarchyNodeViewModel? FindParentNode(HierarchyNodeViewModel targetNode)
    {
        foreach (var root in RootNodes)
        {
            var parent = FindParentNodeRecursive(root, targetNode);
            if (parent != null) return parent;
        }
        return null;
    }

    /// <summary>
    /// Recursively searches for the parent of a node.
    /// </summary>
    private HierarchyNodeViewModel? FindParentNodeRecursive(HierarchyNodeViewModel current, HierarchyNodeViewModel target)
    {
        foreach (var child in current.Children)
        {
            if (child == target)
                return current;

            var found = FindParentNodeRecursive(child, target);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Updates the status text showing component count.
    /// </summary>
    private void UpdateStatusText()
    {
        int count = RootNodes.Count;
        StatusText = count == 0
            ? "No components"
            : $"{count} component{(count == 1 ? "" : "s")}";
    }

    /// <summary>
    /// Expands all nodes in the tree.
    /// </summary>
    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var node in RootNodes)
        {
            ExpandNodeRecursive(node);
        }
    }

    /// <summary>
    /// Collapses all nodes in the tree.
    /// </summary>
    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var node in RootNodes)
        {
            CollapseNodeRecursive(node);
        }
    }

    /// <summary>
    /// Recursively expands a node and its children.
    /// </summary>
    private void ExpandNodeRecursive(HierarchyNodeViewModel node)
    {
        node.IsExpanded = true;
        foreach (var child in node.Children)
        {
            ExpandNodeRecursive(child);
        }
    }

    /// <summary>
    /// Recursively collapses a node and its children.
    /// </summary>
    private void CollapseNodeRecursive(HierarchyNodeViewModel node)
    {
        node.IsExpanded = false;
        foreach (var child in node.Children)
        {
            CollapseNodeRecursive(child);
        }
    }
}
