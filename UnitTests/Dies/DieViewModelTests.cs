using CAP.Avalonia.Commands;
using CAP.Avalonia.Commands.Dies;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Dies;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using CAP_Core.Components.Process;
using Shouldly;
using Xunit;
using static UnitTests.Dies.DieTestFixtures;

namespace UnitTests.Dies;

/// <summary>Die properties, New die flyout and die-link properties — every edit undoable.</summary>
public class DieViewModelTests
{
    private static readonly ProcessGroup SiN = new("SiN 300", new ProcessFingerprint("SiN", 300, "SiO2", 1550, "SiN 300"), new[] { "sin-a", "sin-b" });
    private static readonly ProcessGroup InP = new("InP generic", new ProcessFingerprint("InP", 500, "InP", 1550, "InP generic"), new[] { "inp" });

    [Fact]
    public void NewDie_CreatesADieOfTheChosenProcessAndPreset_NextToTheChip()
    {
        var (canvas, commands) = Canvas();
        var newDie = new NewDieViewModel(canvas, commands, () => new[] { SiN, InP }, () => ActiveProcessSelection.ForGroup(SiN));
        ComponentGroup? created = null;
        newDie.DieCreated = d => created = d;

        newDie.Prepare();
        newDie.SelectedProcess!.Name.ShouldBe("InP generic", "a new die defaults to a process other than the chip's");
        newDie.SelectedSize = newDie.SizeOptions.First(o => o.Name == "4 × 4 mm");
        newDie.CreateCommand.Execute(null);

        created.ShouldNotBeNull();
        created!.ProcessBinding!.DisplayName.ShouldBe("InP generic");
        created.Die!.WidthMicrometers.ShouldBe(4000);
        canvas.Dies.RootDies.Single(d => d != created).ProcessBinding!.DisplayName.ShouldBe("SiN 300",
            "the existing chip becomes Die 1 in the design's process");
        commands.Undo();
        canvas.Dies.IsAssemblyRoot.ShouldBeFalse();
    }

    [Fact]
    public void DieProperties_RenameAndResize_AreUndoable()
    {
        var (canvas, commands, die) = CanvasWithDie();
        var props = new DiePropertiesViewModel(canvas, commands, () => new[] { SiN, InP });
        props.Show(die);

        props.Name = "Logic die";
        props.WidthMillimeters = 1.5;

        die.GroupName.ShouldBe("Logic die");
        die.Die!.WidthMicrometers.ShouldBe(1500);
        commands.Undo();
        die.Die!.WidthMicrometers.ShouldBe(1000);
        commands.Undo();
        die.GroupName.ShouldBe("Die 2");
    }

    [Fact]
    public void DieProperties_ResizeIntoANeighbour_IsRefused()
    {
        var (canvas, commands, die) = CanvasWithDie();
        var props = new DiePropertiesViewModel(canvas, commands, () => new[] { SiN, InP });
        props.Show(canvas.Dies.RootDies.First(d => d != die));

        props.WidthMillimeters = 3.0;

        props.StatusText.ShouldNotBeNullOrEmpty();
        canvas.Dies.RootDies.First(d => d != die).Die!.WidthMicrometers.ShouldBe(2000);
    }

    [Fact]
    public void DieProperties_ProcessAndPdkSubset_ScopeTheDie()
    {
        var (canvas, commands, die) = CanvasWithDie();
        var props = new DiePropertiesViewModel(canvas, commands, () => new[] { SiN, InP });
        props.Show(die);

        props.SelectedProcess = props.ProcessOptions.Single(o => o.Name == "SiN 300");
        props.Pdks.Select(p => p.Name).ShouldBe(new[] { "sin-a", "sin-b" });
        props.Pdks.Single(p => p.Name == "sin-b").IsEnabled = false;

        die.ProcessBinding!.DisplayName.ShouldBe("SiN 300");
        die.Die!.EnabledPdkNames.ShouldBe(new[] { "sin-a" });
        props.PdkSummary.ShouldContain("1");
    }

    [Fact]
    public void DieProperties_LastPdkCannotBeDisabled()
    {
        var (canvas, commands, die) = CanvasWithDie();
        var props = new DiePropertiesViewModel(canvas, commands, () => new[] { SiN, InP });
        props.Show(die);
        props.SelectedProcess = props.ProcessOptions.Single(o => o.Name == "InP generic");

        props.Pdks.Single().IsEnabled = false;

        die.Die!.AllProcessPdksEnabled.ShouldBeTrue();
        props.StatusText.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void LinkDies_CreatesAFacetLink_AndEditingTheGapMovesTheFarDie()
    {
        var (canvas, commands) = Canvas();
        var left = canvas.AddComponent(Die("L", 0, 0, 1000, 600, null, FacingEast("l1", 200))).Component as ComponentGroup;
        var right = canvas.AddComponent(Die("R", 1002, 0, 1000, 600, null, EdgeCoupler("r1", 1002, 200))).Component as ComponentGroup;
        canvas.RefreshDieInterfaces();
        canvas.Dies.ApplyScopeBounds();
        var snap = DieLinkSnapper.Plan(right!, 0, 0, canvas.Dies.RootDies)!;

        commands.ExecuteCommand(LinkDiesCommand.TryCreate(canvas, snap.Pairs)!);

        var link = canvas.ConnectionManager.Connections.Single();
        link.IsCrossChipletFacetLink.ShouldBeTrue();
        LinkDiesCommand.TryCreate(canvas, snap.Pairs).ShouldBeNull("already linked pairs are skipped");

        var props = new DieLinkPropertiesViewModel(canvas, commands);
        props.Show(link);
        props.GapMicrometers.ShouldBe(2.0, 1e-9);
        props.GapMicrometers = 5.0;
        props.MediumIndex = 1.45;

        DieLinkGeometry.Gap(link).ShouldBe(5.0, 1e-6);
        link.FacetGapMediumIndex.ShouldBe(1.45);
        commands.Undo();
        link.FacetGapMediumIndex.ShouldBe(1.0);
        commands.Undo();
        DieLinkGeometry.Gap(link).ShouldBe(2.0, 1e-6);
        commands.Undo();
        canvas.ConnectionManager.Connections.ShouldBeEmpty();
    }

    private static (DesignCanvasViewModel, CommandManager) Canvas()
    {
        var canvas = new DesignCanvasViewModel();
        canvas.SetRootChipSize(2000, 2000);
        return (canvas, new CommandManager());
    }

    private static (DesignCanvasViewModel, CommandManager, ComponentGroup) CanvasWithDie()
    {
        var (canvas, commands) = Canvas();
        var create = new CreateDieCommand(canvas, new DieSpec("Die 2", null, 1000, 1000), new DieSpec("Die 1", null, 2000, 2000));
        commands.ExecuteCommand(create);
        return (canvas, commands, create.CreatedDie!);
    }

    private static Component FacingEast(string id, double y)
    {
        var coupler = EdgeCoupler(id, 1000 - CouplerWidth, y);
        DieEdgeSnapper.Apply(coupler, DieEdgeSnapper.Plan(coupler, new DieOutline(0, 0, 1000, 600), 50)!);
        return coupler;
    }
}
