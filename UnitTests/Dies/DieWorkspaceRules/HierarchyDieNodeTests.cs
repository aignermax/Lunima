using CAP.Avalonia.ViewModels.Hierarchy;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Dies.DieWorkspaceRules;

/// <summary>
/// The hierarchy shows dies as dies — chip icon, "Die · process" kind, process color dot —
/// not as generic groups, and the parts on a die are selectable from it.
/// </summary>
[Collection("LocalizationSingleton")]
public class HierarchyDieNodeTests
{
    [Fact]
    public void DieNodes_AreDies_WithKindAndColor()
    {
        var f = new DieWorkspaceFixture();
        var hierarchy = new HierarchyPanelViewModel(f.Canvas);
        hierarchy.RebuildTree();

        var node = hierarchy.RootNodes.Single(n => ReferenceEquals(n.Component, f.Die1));
        node.IsDie.ShouldBeTrue();
        node.IsPlainGroup.ShouldBeFalse("a die is not shown as a generic group");
        node.TypeLabel.ShouldStartWith("Die");
        node.DieBrush.ShouldNotBeNull();
        node.Children.Count.ShouldBe(2);
    }

    [Fact]
    public void PlainGroup_StaysAGroup()
    {
        var group = new ComponentGroup("G");
        var node = new HierarchyNodeViewModel(group);

        node.IsDie.ShouldBeFalse();
        node.IsPlainGroup.ShouldBeTrue();
        node.TypeLabel.ShouldBe("Group");
        node.DieBrush.ShouldBeNull();
    }

    [Fact]
    public void PartOnADie_IsSelectableFromTheTree()
    {
        var f = new DieWorkspaceFixture();
        var hierarchy = new HierarchyPanelViewModel(f.Canvas);
        hierarchy.RebuildTree();
        var partNode = hierarchy.RootNodes.Single(n => ReferenceEquals(n.Component, f.Die1)).Children
            .Single(c => ReferenceEquals(c.Component, f.A));

        partNode.ComponentViewModel.ShouldNotBeNull();
        partNode.IsSelected = true;

        f.Canvas.SelectedComponent!.Component.ShouldBe(f.A);
    }

    [Fact]
    public void PartPlacedOnADie_ShowsUpInTheTree()
    {
        var f = new DieWorkspaceFixture();
        var hierarchy = new HierarchyPanelViewModel(f.Canvas);
        hierarchy.RebuildTree();

        f.Die2.AddChild(DieTestFixtures.Part("c", f.Die2.PhysicalX + 100, f.Die2.PhysicalY + 100));
        f.Canvas.NotifyDiesChanged();

        hierarchy.RootNodes.Single(n => ReferenceEquals(n.Component, f.Die2)).Children.ShouldHaveSingleItem();
    }
}
