using System.Collections.Specialized;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Hierarchy;

public partial class HierarchyPanelViewModel
{
    /// <summary>Root additions/removals preserve every unaffected subtree and its bindings.</summary>
    private void OnComponentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            int index = _canvas.Components.Take(e.NewStartingIndex).Count(IsHierarchyRoot);
            foreach (ComponentViewModel vm in e.NewItems)
            {
                if (IsHierarchyRoot(vm))
                    RootNodes.Insert(index++, CreateNodeRecursive(vm.Component, vm));
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            foreach (ComponentViewModel vm in e.OldItems)
            {
                var node = RootNodes.FirstOrDefault(n => ReferenceEquals(n.Component, vm.Component));
                if (node == null) continue;
                node.Dispose();
                RootNodes.Remove(node);
            }
        }
        else
        {
            RebuildTree();
            return;
        }
        // Root changes do not reconcile retained die subtrees. Their content can change
        // during a lift/delete command; DiesChanged must still detect that difference.
    }

    private bool IsHierarchyRoot(ComponentViewModel vm) =>
        vm.Component.ParentGroup == null || vm.Component.ParentGroup == _canvas.CurrentEditGroup;
}
