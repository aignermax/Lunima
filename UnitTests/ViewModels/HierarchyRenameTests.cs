using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.ViewModels;

/// <summary>
/// Tests for inline rename feature in the Hierarchy Panel (issue #278).
/// Covers HierarchyNodeViewModel rename state, HierarchyPanelViewModel wiring,
/// and RenameComponentCommand undo/redo.
/// </summary>
public class HierarchyRenameTests
{
    // ─── HierarchyNodeViewModel rename state ─────────────────────────────────

    [Fact]
    public void StartRename_SetsIsRenamingTrue()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        var node = new HierarchyNodeViewModel(comp);

        node.StartRenameCommand.Execute(null);

        node.IsRenaming.ShouldBeTrue();
    }

    [Fact]
    public void StartRename_PreFillsEditNameWithCurrentDisplayName()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        var node = new HierarchyNodeViewModel(comp);

        node.StartRenameCommand.Execute(null);

        node.EditName.ShouldBe(comp.Identifier);
    }

    [Fact]
    public void StartRename_WithHumanReadableName_PreFillsHumanReadableName()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.HumanReadableName = "InputCoupler";
        var node = new HierarchyNodeViewModel(comp);

        node.StartRenameCommand.Execute(null);

        node.EditName.ShouldBe("InputCoupler");
    }

    [Fact]
    public void StartRename_ForGroup_PreFillsGroupName()
    {
        var group = new ComponentGroup("MyGroup");
        var node = new HierarchyNodeViewModel(group);

        node.StartRenameCommand.Execute(null);

        node.EditName.ShouldBe("MyGroup");
    }

    [Fact]
    public void CancelRename_SetsIsRenamingFalse()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        var node = new HierarchyNodeViewModel(comp);
        node.StartRenameCommand.Execute(null);

        node.CancelRenameCommand.Execute(null);

        node.IsRenaming.ShouldBeFalse();
    }

    [Fact]
    public void CancelRename_DoesNotCallRenameConfirmed()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        var node = new HierarchyNodeViewModel(comp);
        bool callbackFired = false;
        node.RenameConfirmed = (_, _) => callbackFired = true;

        node.StartRenameCommand.Execute(null);
        node.EditName = "SomeName";
        node.CancelRenameCommand.Execute(null);

        callbackFired.ShouldBeFalse();
    }

    [Fact]
    public void ConfirmRename_WithValidName_CallsRenameConfirmed()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        var node = new HierarchyNodeViewModel(comp);
        string? receivedName = null;
        node.RenameConfirmed = (_, name) => receivedName = name;

        node.StartRenameCommand.Execute(null);
        node.EditName = "OutputCoupler";
        node.ConfirmRenameCommand.Execute(null);

        receivedName.ShouldBe("OutputCoupler");
    }

    [Fact]
    public void ConfirmRename_TrimsWhitespace()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        var node = new HierarchyNodeViewModel(comp);
        string? receivedName = null;
        node.RenameConfirmed = (_, name) => receivedName = name;

        node.StartRenameCommand.Execute(null);
        node.EditName = "  MyComponent  ";
        node.ConfirmRenameCommand.Execute(null);

        receivedName.ShouldBe("MyComponent");
    }

    [Fact]
    public void ConfirmRename_SetsIsRenamingFalse()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        var node = new HierarchyNodeViewModel(comp);
        node.RenameConfirmed = (_, _) => { };

        node.StartRenameCommand.Execute(null);
        node.EditName = "ValidName";
        node.ConfirmRenameCommand.Execute(null);

        node.IsRenaming.ShouldBeFalse();
    }

    [Fact]
    public void ConfirmRename_WithEmptyName_DoesNotCallRenameConfirmed()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        var node = new HierarchyNodeViewModel(comp);
        bool callbackFired = false;
        node.RenameConfirmed = (_, _) => callbackFired = true;

        node.StartRenameCommand.Execute(null);
        node.EditName = "   ";
        node.ConfirmRenameCommand.Execute(null);

        callbackFired.ShouldBeFalse();
        node.IsRenaming.ShouldBeFalse();
    }

    // ─── HierarchyPanelViewModel integration ─────────────────────────────────

    [Fact]
    public void HierarchyPanel_ConfirmRename_UpdatesComponentHumanReadableName()
    {
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var comp = TestComponentFactory.CreateStraightWaveGuide();
        canvas.AddComponent(comp, "Waveguide");
        hierarchy.RebuildTree();

        var node = hierarchy.RootNodes[0];
        node.StartRenameCommand.Execute(null);
        node.EditName = "InputCoupler";
        node.ConfirmRenameCommand.Execute(null);

        comp.HumanReadableName.ShouldBe("InputCoupler");
    }

    [Fact]
    public void HierarchyPanel_ConfirmRename_RefreshesDisplayName()
    {
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var comp = TestComponentFactory.CreateStraightWaveGuide();
        canvas.AddComponent(comp, "Waveguide");
        hierarchy.RebuildTree();

        var node = hierarchy.RootNodes[0];
        node.StartRenameCommand.Execute(null);
        node.EditName = "OutputWG";
        node.ConfirmRenameCommand.Execute(null);

        node.DisplayName.ShouldBe("OutputWG");
    }

    [Fact]
    public void HierarchyPanel_ConfirmGroupRename_UpdatesGroupName()
    {
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        var group = new ComponentGroup("OriginalName");
        canvas.AddComponent(group, "Group");
        hierarchy.RebuildTree();

        var node = hierarchy.RootNodes[0];
        node.StartRenameCommand.Execute(null);
        node.EditName = "RenamedGroup";
        node.ConfirmRenameCommand.Execute(null);

        group.GroupName.ShouldBe("RenamedGroup");
    }

    [Fact]
    public void HierarchyPanel_RenameComponentCallback_IsInvoked()
    {
        var canvas = new DesignCanvasViewModel();
        var hierarchy = new HierarchyPanelViewModel(canvas);

        Component? renamedComponent = null;
        string? renamedTo = null;
        hierarchy.RenameComponent = (comp, name) =>
        {
            renamedComponent = comp;
            renamedTo = name;
        };

        var comp = TestComponentFactory.CreateStraightWaveGuide();
        canvas.AddComponent(comp, "Waveguide");
        hierarchy.RebuildTree();

        var node = hierarchy.RootNodes[0];
        node.StartRenameCommand.Execute(null);
        node.EditName = "CustomName";
        node.ConfirmRenameCommand.Execute(null);

        renamedComponent.ShouldBe(comp);
        renamedTo.ShouldBe("CustomName");
    }

    // ─── RenameComponentCommand undo/redo ─────────────────────────────────────

    [Fact]
    public void RenameComponentCommand_Execute_SetsHumanReadableName()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        var cmd = new RenameComponentCommand(comp, "NewName");

        cmd.Execute();

        comp.HumanReadableName.ShouldBe("NewName");
    }

    [Fact]
    public void RenameComponentCommand_Undo_RestoresHumanReadableName()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.HumanReadableName = "OriginalName";
        var cmd = new RenameComponentCommand(comp, "NewName");

        cmd.Execute();
        cmd.Undo();

        comp.HumanReadableName.ShouldBe("OriginalName");
    }

    [Fact]
    public void RenameComponentCommand_Undo_RestoresNullHumanReadableName()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        // HumanReadableName starts as null
        var cmd = new RenameComponentCommand(comp, "NewName");

        cmd.Execute();
        cmd.Undo();

        comp.HumanReadableName.ShouldBeNull();
    }

    [Fact]
    public void RenameComponentCommand_ForGroup_Execute_SetsGroupName()
    {
        var group = new ComponentGroup("OldName");
        var cmd = new RenameComponentCommand(group, "NewGroupName");

        cmd.Execute();

        group.GroupName.ShouldBe("NewGroupName");
    }

    [Fact]
    public void RenameComponentCommand_ForGroup_Undo_RestoresGroupName()
    {
        var group = new ComponentGroup("OldName");
        var cmd = new RenameComponentCommand(group, "NewGroupName");

        cmd.Execute();
        cmd.Undo();

        group.GroupName.ShouldBe("OldName");
    }

    [Fact]
    public void RenameComponentCommand_Description_ContainsNewName()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        var cmd = new RenameComponentCommand(comp, "SpecialName");

        cmd.Description.ShouldContain("SpecialName");
    }
}
