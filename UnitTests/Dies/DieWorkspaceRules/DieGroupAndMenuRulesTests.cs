using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Dies.DieWorkspaceRules;

/// <summary>
/// Dies are the top level of an assembly: they are never grouped, and the group-only and
/// component-only actions (Ungroup, Rename group, Make gate, Edit component — a die has no
/// S-matrix) are unavailable on a die, each refusal with a short reason.
/// </summary>
[Collection("LocalizationSingleton")]
public class DieGroupAndMenuRulesTests
{
    [Fact]
    public void Group_OnASelectionWithADie_IsDisabledWithAReason()
    {
        var f = new DieWorkspaceFixture();
        f.Vm.WorkspaceSwitch.Current = Workspace.System;
        f.Canvas.Selection.SelectSingle(f.DieVm(f.Die1));
        f.Canvas.Selection.AddToSelection(f.DieVm(f.Die2));
        f.Canvas.SelectedComponent = f.DieVm(f.Die2);
        var interaction = f.Vm.CanvasInteraction;

        interaction.SelectionHasDie.ShouldBeTrue();
        interaction.CreateGroupCommand.CanExecute(null).ShouldBeFalse();
        interaction.GroupBlockedReason.ShouldNotBeNullOrWhiteSpace();

        int before = f.Canvas.Components.Count;
        f.Vm.CreateGroupCommand.Execute(null);
        f.Canvas.Components.Count.ShouldBe(before, "Ctrl+G on dies groups nothing");
        f.Vm.StatusText.ShouldBe(interaction.GroupBlockedReason);
        f.Canvas.Components.ShouldAllBe(c => c.Component.ParentGroup == null);
    }

    [Fact]
    public void Die_OffersNoUngroupRenameEditComponentOrMakeGate()
    {
        var f = new DieWorkspaceFixture();
        f.Vm.WorkspaceSwitch.Current = Workspace.System;
        f.Select(f.DieVm(f.Die2));
        var interaction = f.Vm.CanvasInteraction;

        interaction.IsDieSelected.ShouldBeTrue("the context menu hides component-only entries");
        interaction.UngroupCommand.CanExecute(null).ShouldBeFalse();
        interaction.RenameGroupCommand.CanExecute(null).ShouldBeFalse();
        interaction.OpenSelectedComponentSettingsCommand.CanExecute(null).ShouldBeFalse("a die has no S-matrix to edit");

        f.Vm.UngroupCommand.Execute(null);
        f.Canvas.Dies.RootDies.ShouldContain(f.Die2, "a die is never dissolved like a group");
        f.Vm.MakeGateCommand.Execute(null);
        f.Vm.RightPanel.MakeGate.CanMakeGate.ShouldBeFalse();
    }

    [Fact]
    public void PartsOnOneDie_StillGroup()
    {
        var f = new DieWorkspaceFixture();
        f.Canvas.Selection.SelectSingle(f.PartVm(f.A));
        f.Canvas.Selection.AddToSelection(f.PartVm(f.B));

        f.Vm.CanvasInteraction.CreateGroupCommand.CanExecute(null).ShouldBeTrue();
        f.Vm.CanvasInteraction.CreateGroupCommand.Execute(null);

        f.Die1.ChildComponents.ShouldHaveSingleItem().ShouldBeOfType<ComponentGroup>();
    }
}
