using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Dies.DieWorkspaceRules;

/// <summary>
/// Every design is an assembly of at least one die: a design without die data (every older
/// file) shows its chip as "Die 1" with its size and process — selectable and framed in System,
/// with die properties — while the model, the file and the exports stay single-die until a die
/// property is edited. That first edit makes the chip a real die in one undo step.
/// </summary>
[Collection("LocalizationSingleton")]
public class ChipDieTests
{
    private const string LegacyExample = "Mach-Zehnder Interferometer.lun";

    [Fact]
    public async Task LegacyFile_ShowsItsChipAsDie1_WithoutChangingTheDesign()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        (await vm.FileOperations.LoadDesignFromPathAsync(Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), LegacyExample)))
            .ShouldBeTrue();
        var componentsBefore = vm.Canvas.Components.Count;

        var chip = vm.Canvas.ChipDie;
        chip.IsShown.ShouldBeTrue();
        chip.Die.GroupName.ShouldBe("Die 1");
        chip.Outline.Width.ShouldBe(vm.Canvas.ChipMaxX - vm.Canvas.ChipMinX, 1e-6);
        chip.Die.ProcessBinding?.DisplayName.ShouldBe(vm.FileOperations.ActiveProcess?.DisplayName);
        vm.Canvas.Dies.IsAssemblyRoot.ShouldBeFalse("the model stays single-die");
        vm.Canvas.Components.Count.ShouldBe(componentsBefore);
        vm.FileOperations.HasUnsavedChanges.ShouldBeFalse("showing Die 1 does not touch the file");
    }

    [Fact]
    public void System_ClickOnTheChip_SelectsDie1_WithItsProperties()
    {
        var vm = SingleDie(out _);
        vm.WorkspaceSwitch.Current = Workspace.System;

        vm.CanvasInteraction.CanvasClicked(1500, 1500);

        vm.Canvas.ChipDie.IsSelected.ShouldBeTrue();
        vm.DieProperties.Die.ShouldBe(vm.Canvas.ChipDie.Die);
        vm.DieProperties.WidthMillimeters.ShouldBe(2.0, 1e-9);
        vm.DieProperties.CanDelete.ShouldBeFalse("a design keeps its one die");
        vm.CanvasInteraction.DeleteSelectedCommand.Execute(null);
        vm.Canvas.Components.Count.ShouldBe(1, "Delete leaves the chip alone");
    }

    [Fact]
    public void Layout_ClickOnTheChip_SelectsNothing()
    {
        var vm = SingleDie(out _);

        vm.CanvasInteraction.CanvasClicked(1500, 1500);

        vm.Canvas.SelectedComponent.ShouldBeNull();
    }

    [Fact]
    public async Task FirstEditOfDie1_MakesItARealDie_InOneUndoStep()
    {
        var vm = SingleDie(out var part);
        vm.WorkspaceSwitch.Current = Workspace.System;
        vm.CanvasInteraction.CanvasClicked(1500, 1500);

        vm.DieProperties.Name = "Laser chip";

        var die = vm.Canvas.Dies.RootDies.ShouldHaveSingleItem();
        die.GroupName.ShouldBe("Laser chip");
        part.ParentGroup.ShouldBe(die);
        die.Die!.WidthMicrometers.ShouldBe(2000, 1e-6);
        vm.Canvas.SelectedComponent!.Component.ShouldBe(die, "the new die stays selected");

        await vm.UndoCommand.ExecuteAsync(null);

        vm.Canvas.Dies.IsAssemblyRoot.ShouldBeFalse();
        part.ParentGroup.ShouldBeNull("undo restores the single-die design exactly");
        vm.Canvas.Components.ShouldHaveSingleItem().Component.ShouldBe(part);
    }

    [Fact]
    public void NewDie_TurnsTheChipDieIntoARealDie()
    {
        var vm = SingleDie(out _);
        vm.WorkspaceSwitch.Current = Workspace.System;
        vm.CanvasInteraction.CanvasClicked(1500, 1500);

        vm.NewDie.Prepare();
        vm.NewDie.CreateCommand.Execute(null);

        vm.Canvas.ChipDie.IsShown.ShouldBeFalse();
        vm.Canvas.ChipDie.IsSelected.ShouldBeFalse("the stand-in is not selected once real dies exist");
        vm.Canvas.Dies.RootDies.Count.ShouldBe(2);
    }

    private static CAP.Avalonia.ViewModels.MainViewModel SingleDie(out Component part)
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.WorkspaceSwitch.Current = Workspace.Layout;
        vm.Canvas.SetRootChipSize(2000, 2000);
        part = vm.Canvas.AddComponent(DieTestFixtures.Part("p", 200, 200)).Component;
        return vm;
    }
}
