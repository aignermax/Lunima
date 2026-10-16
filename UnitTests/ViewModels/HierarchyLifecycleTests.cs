using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

public class HierarchyLifecycleTests
{
    [Fact]
    public void AddingAndRemovingRoots_DoesNotRebuildUnchangedHierarchy()
    {
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);
        canvas.AddComponent(new ComponentGroup("kept"));
        var kept = hierarchy.RootNodes.Single();
        int resets = 0;
        hierarchy.RootNodes.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };
        for (int i = 0; i < 30; i++)
            canvas.AddComponent(new ComponentGroup($"part {i}"));
        for (int i = 0; i < 30; i++)
            canvas.Components.RemoveAt(canvas.Components.Count - 1);
        hierarchy.RootNodes.Single().ShouldBeSameAs(kept);
        resets.ShouldBe(0, "each added/removed root must touch only its own hierarchy subtree");
    }

    [Fact]
    public void ReplacedHierarchyNodes_AreCollectibleWhileTheirGroupsRemainAlive()
    {
        var canvas = new DesignCanvasViewModel();
        var group = new ComponentGroup("root");
        group.AddChildren(new[] { new ComponentGroup("nested") });
        canvas.AddComponent(group);
        var hierarchy = new HierarchyPanelViewModel(canvas);
        hierarchy.RebuildTree();
        var old = ReplaceTree(hierarchy);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        old.Root.IsAlive.ShouldBeFalse();
        old.Child.IsAlive.ShouldBeFalse();
        group.GroupName = "renamed";
        hierarchy.RootNodes.Single().DisplayName.ShouldBe("renamed (1)");
        GC.KeepAlive(group);
        GC.KeepAlive(hierarchy);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Root, WeakReference Child) ReplaceTree(HierarchyPanelViewModel hierarchy)
    {
        var node = hierarchy.RootNodes.Single();
        var refs = (new WeakReference(node), new WeakReference(node.Children.Single()));
        hierarchy.RebuildTree();
        return refs;
    }
}
