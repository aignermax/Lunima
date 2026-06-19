using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests;

namespace UnitTests.ViewModels;

/// <summary>
/// Tests for bidirectional selection synchronisation between the
/// hierarchy panel and the design canvas.
/// </summary>
public class SelectionSyncTests
{
    // ── helpers ────────────────────────────────────────────────────────────

    private static (DesignCanvasViewModel canvas, HierarchyPanelViewModel hierarchy) CreateSetup()
    {
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);
        return (canvas, hierarchy);
    }

    // ── 1. Hierarchy → Canvas ──────────────────────────────────────────────

    /// <summary>
    /// Clicking a node in the hierarchy must update <see cref="DesignCanvasViewModel.SelectedComponent"/>.
    /// </summary>
    [Fact]
    public void Hierarchy_SelectNode_UpdatesCanvasSelection()
    {
        var (canvas, hierarchy) = CreateSetup();

        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        var comp2 = TestComponentFactory.CreateStraightWaveGuide();
        var vm1 = canvas.AddComponent(comp1, "Waveguide1");
        var vm2 = canvas.AddComponent(comp2, "Waveguide2");
        hierarchy.RebuildTree();

        // Act – click the second node in the hierarchy
        hierarchy.RootNodes[1].SelectCommand.Execute(null);

        // Assert – canvas selection must reflect the hierarchy choice
        canvas.SelectedComponent.ShouldBe(vm2);
        vm2.IsSelected.ShouldBeTrue();
        canvas.Selection.SelectedComponents.ShouldContain(vm2);
        vm1.IsSelected.ShouldBeFalse();
    }

    // ── 2. Canvas → Hierarchy ──────────────────────────────────────────────

    /// <summary>
    /// Setting <see cref="DesignCanvasViewModel.SelectedComponent"/> (as the canvas does on click)
    /// must highlight the matching node in the hierarchy tree.
    /// </summary>
    [Fact]
    public void Canvas_SelectComponent_HighlightsHierarchyNode()
    {
        var (canvas, hierarchy) = CreateSetup();

        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        var comp2 = TestComponentFactory.CreateStraightWaveGuide();
        var vm1 = canvas.AddComponent(comp1, "Waveguide1");
        var vm2 = canvas.AddComponent(comp2, "Waveguide2");
        hierarchy.RebuildTree();

        // Act – simulate canvas click selecting the second component
        canvas.SelectedComponent = vm2;

        // Assert – second node should be highlighted, first deselected
        hierarchy.RootNodes[0].IsSelected.ShouldBeFalse();
        hierarchy.RootNodes[1].IsSelected.ShouldBeTrue();
    }

    // ── 3. Loop prevention ────────────────────────────────────────────────

    /// <summary>
    /// A bidirectional sync must not cause an infinite loop or stack-overflow
    /// when selection toggles back and forth.
    /// </summary>
    [Fact]
    public void BidirectionalChange_DoesNotLoop()
    {
        var (canvas, hierarchy) = CreateSetup();

        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        var comp2 = TestComponentFactory.CreateStraightWaveGuide();
        var vm1 = canvas.AddComponent(comp1, "Waveguide1");
        var vm2 = canvas.AddComponent(comp2, "Waveguide2");
        hierarchy.RebuildTree();

        // Act – alternate between hierarchy and canvas selection several times.
        // If there is an infinite loop this will throw StackOverflowException.
        hierarchy.RootNodes[0].SelectCommand.Execute(null);
        canvas.SelectedComponent = vm2;
        hierarchy.RootNodes[1].SelectCommand.Execute(null);
        canvas.SelectedComponent = vm1;

        // Assert – final state is consistent
        canvas.SelectedComponent.ShouldBe(vm1);
        hierarchy.RootNodes[0].IsSelected.ShouldBeTrue();
        hierarchy.RootNodes[1].IsSelected.ShouldBeFalse();
    }

    // ── 4. Group child Canvas → Hierarchy ─────────────────────────────────

    /// <summary>
    /// Selecting a group component on the canvas must highlight its node in the
    /// hierarchy even when the tree contains nested children.
    /// </summary>
    [Fact]
    public void Canvas_SelectGroupChild_HierarchyNodeIsHighlighted()
    {
        var (canvas, hierarchy) = CreateSetup();

        var child1 = TestComponentFactory.CreateStraightWaveGuide();
        var child2 = TestComponentFactory.CreateStraightWaveGuide();

        var group = new ComponentGroup("TestGroup");
        group.AddChild(child1);
        group.AddChild(child2);

        var groupVm = canvas.AddComponent(group, "Group");
        hierarchy.RebuildTree();

        // Act – select the group on the canvas
        canvas.SelectedComponent = groupVm;

        // Assert – group node highlighted, children not highlighted
        hierarchy.RootNodes[0].IsSelected.ShouldBeTrue();
        hierarchy.RootNodes[0].Children[0].IsSelected.ShouldBeFalse();
        hierarchy.RootNodes[0].Children[1].IsSelected.ShouldBeFalse();
    }

    // ── 5. Auto-expand collapsed parent ───────────────────────────────────

    /// <summary>
    /// When the canvas selects a component that lives inside a collapsed group,
    /// the parent group node must be auto-expanded so the selected node is visible
    /// in the hierarchy tree (Canvas → Hierarchy direction with nested component).
    /// </summary>
    [Fact]
    public void Hierarchy_SelectInsideCollapsedGroup_AutoExpandsParents()
    {
        var (canvas, hierarchy) = CreateSetup();

        var child = TestComponentFactory.CreateStraightWaveGuide();
        var group = new ComponentGroup("TestGroup");
        group.AddChild(child);

        // Add both group and child to canvas so the child gets a ComponentViewModel.
        // In RebuildTree the child is filtered from root nodes (ParentGroup != null)
        // but appears as a child node of the group node, with its ComponentViewModel set.
        var groupVm = canvas.AddComponent(group, "Group");
        var childVm = canvas.AddComponent(child, "Child");
        hierarchy.RebuildTree();

        var groupNode = hierarchy.RootNodes[0];
        groupNode.IsExpanded = false; // collapse the group

        // Act – canvas selects the child component (e.g. in group-edit mode)
        canvas.SelectedComponent = childVm;

        // Assert – the parent group node must be expanded so the child is visible
        groupNode.IsExpanded.ShouldBeTrue();
        // The child node inside the group is highlighted
        groupNode.Children[0].IsSelected.ShouldBeTrue();
    }
}
