using Avalonia;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels.Workspaces;
using Shouldly;
using Xunit;

namespace UnitTests.Dies.DieWorkspaceRules;

/// <summary>
/// Outside the System workspace a die is the background the parts sit on: it is never picked,
/// dragged, deleted or edited there, a click on its empty area deselects, and the properties
/// panel shows it read-only at most — a selected part keeps the panel. In System the die is
/// the unit of work again.
/// </summary>
[Collection("LocalizationSingleton")]
public class InertDieTests
{
    [Theory]
    [InlineData(Workspace.Layout)]
    [InlineData(Workspace.Optics)]
    [InlineData(Workspace.Logic)]
    public void EmptyDieArea_IsNotHit_AndAClickDeselects(Workspace workspace)
    {
        var f = new DieWorkspaceFixture();
        f.Vm.WorkspaceSwitch.Current = workspace;
        var o = DieWorkspaceFixture.Outline(f.Die2);
        f.Select(f.PartVm(f.A));

        DesignCanvasHitTesting.HitTestComponent(new Point(o.CenterX, o.CenterY), f.Canvas).ShouldBeNull();
        DesignCanvasHitTesting.HitTestGroupLabel(new Point(o.X + 1, o.Y - 5), f.Canvas).ShouldBeNull("the die label is not a handle either");
        f.Vm.CanvasInteraction.CanvasClicked(o.CenterX, o.CenterY);

        f.Canvas.SelectedComponent.ShouldBeNull("a click on an empty die area selects nothing");
        f.Canvas.Selection.SelectedComponents.ShouldBeEmpty();
    }

    [Fact]
    public void System_ClickOnTheDie_SelectsIt()
    {
        var f = new DieWorkspaceFixture();
        f.Vm.WorkspaceSwitch.Current = Workspace.System;
        var o = DieWorkspaceFixture.Outline(f.Die2);

        f.Vm.CanvasInteraction.CanvasClicked(o.CenterX, o.CenterY);

        f.Canvas.SelectedComponent!.Component.ShouldBe(f.Die2);
        f.Vm.DieProperties.Die.ShouldBe(f.Die2);
        f.Vm.DieProperties.IsEditable.ShouldBeTrue();
    }

    [Fact]
    public void SelectedPart_KeepsThePropertiesPanel_TheLibraryStillFollowsItsDie()
    {
        var f = new DieWorkspaceFixture();

        f.Select(f.PartVm(f.A));

        f.Vm.DieProperties.IsVisible.ShouldBeFalse("a selected part takes priority over its die");
        f.Vm.ScopeDie.ShouldBe(f.Die1, "the library still follows the part's die");
    }

    [Fact]
    public void DieSelectedOutsideSystem_IsReadOnly_AndDeleteLeavesIt()
    {
        var f = new DieWorkspaceFixture();
        f.Select(f.DieVm(f.Die2));

        f.Vm.DieProperties.IsVisible.ShouldBeTrue();
        f.Vm.DieProperties.IsEditable.ShouldBeFalse();
        f.Vm.DieProperties.CanDelete.ShouldBeFalse();
        f.Vm.DieProperties.Name = "Renamed";
        f.Die2.GroupName.ShouldBe("Die 2", "read-only outside System");

        f.Vm.CanvasInteraction.DeleteSelectedCommand.Execute(null);

        f.Canvas.Dies.RootDies.ShouldContain(f.Die2);
        f.Vm.StatusText.ShouldContain("System");
    }

    [Fact]
    public async Task SwitchingToSystem_MakesTheDieEditable()
    {
        var f = new DieWorkspaceFixture();
        f.Select(f.DieVm(f.Die2));
        f.Vm.WorkspaceSwitch.Current = Workspace.System;

        f.Vm.DieProperties.IsEditable.ShouldBeTrue();
        f.Vm.DieProperties.Name = "Laser die";

        f.Die2.GroupName.ShouldBe("Laser die");
        await f.Vm.UndoCommand.ExecuteAsync(null);
        f.Die2.GroupName.ShouldBe("Die 2", "one undo step per edit");
    }
}
