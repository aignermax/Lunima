using CAP.Avalonia.ViewModels.Shell;
using CAP.Avalonia.ViewModels.Workspaces;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels.Shell;

/// <summary>
/// The contextual left rail: each workspace opens on its own section, orders the icons for
/// it, never drops a section, and remembers the user's last choice per workspace.
/// </summary>
public class WorkspaceLeftRailTests
{
    [Theory]
    [InlineData(Workspace.Layout, LeftRailSection.Library)]
    [InlineData(Workspace.Optics, LeftRailSection.Library)]
    [InlineData(Workspace.Logic, LeftRailSection.Gates)]
    [InlineData(Workspace.System, LeftRailSection.Chiplets)]
    public void EachWorkspace_OpensOnItsDefaultSection(Workspace workspace, LeftRailSection expected)
    {
        var rail = new LeftRailViewModel();

        rail.ApplyWorkspace(workspace);

        rail.ActiveSection.ShouldBe(expected);
        WorkspaceRailLayout.DefaultSection(workspace).ShouldBe(expected);
    }

    [Theory]
    [InlineData(Workspace.Layout)]
    [InlineData(Workspace.Optics)]
    [InlineData(Workspace.Logic)]
    [InlineData(Workspace.System)]
    public void EveryWorkspace_ListsEverySection_ExactlyOnce(Workspace workspace)
    {
        WorkspaceRailLayout.Order(workspace).OrderBy(s => s)
            .ShouldBe(Enum.GetValues<LeftRailSection>().OrderBy(s => s), "no panel may become unreachable");
    }

    [Fact]
    public void IconOrder_FollowsTheWorkspace()
    {
        var rail = new LeftRailViewModel();

        rail.ApplyWorkspace(Workspace.Logic);
        rail.GatesOrder.ShouldBe(0);
        rail.LibraryOrder.ShouldBe(1);

        rail.ApplyWorkspace(Workspace.System);
        rail.ChipletsOrder.ShouldBe(0);
        rail.LibraryOrder.ShouldBe(1);

        rail.ApplyWorkspace(Workspace.Layout);
        new[] { rail.LibraryOrder, rail.HierarchyOrder, rail.PdksOrder, rail.GatesOrder, rail.ChipletsOrder }
            .ShouldBe(new[] { 0, 1, 2, 3, 4 });
    }

    [Fact]
    public void TheRail_RemembersTheChoice_PerWorkspace()
    {
        var rail = new LeftRailViewModel();
        rail.ApplyWorkspace(Workspace.Logic);
        rail.ShowHierarchyCommand.Execute(null);
        rail.ApplyWorkspace(Workspace.Layout);
        rail.ShowPdksCommand.Execute(null);

        rail.ApplyWorkspace(Workspace.Logic);
        rail.IsHierarchyActive.ShouldBeTrue("Logic remembers Hierarchy");

        rail.ApplyWorkspace(Workspace.Layout);
        rail.IsPdksActive.ShouldBeTrue("Layout remembers PDKs");

        rail.ApplyWorkspace(Workspace.System);
        rail.IsChipletsActive.ShouldBeTrue("an unvisited workspace starts on its default");
    }

    [Fact]
    public void NewSections_HaveCommandsAndExclusiveFlags()
    {
        var rail = new LeftRailViewModel();

        rail.ShowGatesCommand.Execute(null);
        rail.IsGatesActive.ShouldBeTrue();
        rail.IsLibraryActive.ShouldBeFalse();

        rail.ShowChipletsCommand.Execute(null);
        rail.IsChipletsActive.ShouldBeTrue();
        rail.IsGatesActive.ShouldBeFalse();
    }
}
