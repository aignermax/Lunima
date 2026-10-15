using CAP.Avalonia.ViewModels.Shell;
using CAP.Avalonia.ViewModels.Workspaces;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels.Shell;

/// <summary>
/// The contextual left rail: each workspace opens on its own section, shows the sections of
/// its tool set in its order (Layout shows all), and remembers the user's last choice per
/// workspace. A section the workspace leaves out is requested through a switch to Layout.
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

    [Fact]
    public void Layout_ListsEverySection_ExactlyOnce()
    {
        WorkspaceRailLayout.Order(Workspace.Layout).OrderBy(s => s)
            .ShouldBe(Enum.GetValues<LeftRailSection>().OrderBy(s => s), "Layout is the one-click way to every panel");
    }

    [Theory]
    [InlineData(Workspace.Optics, new[] { LeftRailSection.Library, LeftRailSection.Hierarchy, LeftRailSection.Pdks })]
    [InlineData(Workspace.Logic, new[] { LeftRailSection.Gates, LeftRailSection.Hierarchy })]
    [InlineData(Workspace.System, new[] { LeftRailSection.Chiplets, LeftRailSection.Library, LeftRailSection.Hierarchy, LeftRailSection.Pdks })]
    public void OtherWorkspaces_ShowOnlyTheirToolSetSections(Workspace workspace, LeftRailSection[] expected)
    {
        var rail = new LeftRailViewModel();
        rail.ApplyWorkspace(workspace);

        WorkspaceRailLayout.Order(workspace).ShouldBe(expected);
        rail.IsLibraryAvailable.ShouldBe(expected.Contains(LeftRailSection.Library));
        rail.IsGatesAvailable.ShouldBe(expected.Contains(LeftRailSection.Gates));
        rail.IsChipletsAvailable.ShouldBe(expected.Contains(LeftRailSection.Chiplets));
    }

    [Fact]
    public void RequestingAHiddenSection_AsksForAWorkspaceThatShowsIt()
    {
        var rail = new LeftRailViewModel();
        rail.ApplyWorkspace(Workspace.Logic);
        LeftRailSection? requested = null;
        rail.SectionUnavailable += section =>
        {
            requested = section;
            rail.ApplyWorkspace(Workspace.Layout);
        };

        rail.ShowLibraryCommand.Execute(null);

        requested.ShouldBe(LeftRailSection.Library);
        rail.IsLibraryActive.ShouldBeTrue("the library opens once the host switched to Layout");
    }

    [Fact]
    public void RequestingAHiddenSection_WithoutAHandler_LeavesThePaneUnchanged()
    {
        var rail = new LeftRailViewModel();
        rail.ApplyWorkspace(Workspace.System);

        rail.ShowGatesCommand.Execute(null);

        rail.IsChipletsActive.ShouldBeTrue();
    }

    [Fact]
    public void IconOrder_FollowsTheWorkspace()
    {
        var rail = new LeftRailViewModel();

        rail.ApplyWorkspace(Workspace.Logic);
        rail.GatesOrder.ShouldBe(0);
        rail.HierarchyOrder.ShouldBe(1);

        rail.ApplyWorkspace(Workspace.System);
        rail.ChipletsOrder.ShouldBe(0);
        rail.LibraryOrder.ShouldBe(1, "System's library holds the edge couplers that link dies");
        rail.HierarchyOrder.ShouldBe(2);

        rail.ApplyWorkspace(Workspace.Layout);
        new[] { rail.LibraryOrder, rail.PdksOrder, rail.HierarchyOrder, rail.GatesOrder, rail.ChipletsOrder }
            .ShouldBe(new[] { 0, 1, 2, 3, 4 }, "Layout keeps the fabrication context (PDKs) under the library");

        rail.ApplyWorkspace(Workspace.Optics);
        new[] { rail.LibraryOrder, rail.HierarchyOrder, rail.PdksOrder }.ShouldBe(new[] { 0, 1, 2 });
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
