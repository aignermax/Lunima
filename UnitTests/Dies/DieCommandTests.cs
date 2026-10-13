using CAP.Avalonia.Commands;
using CAP.Avalonia.Commands.Dies;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using Shouldly;
using UnitTests.Components;
using UnitTests.Integration;
using Xunit;
using static UnitTests.Dies.DieTestFixtures;

namespace UnitTests.Dies;

/// <summary>
/// The canvas side of dies: New die turns a single-die design into an assembly (undo restores
/// it), parts dropped onto a die join it, and entering a die bounds work by its outline.
/// </summary>
public class DieCommandTests
{
    private const double ChipSize = 2000;

    [Fact]
    public void NewDie_InSingleDieDesign_ConvertsTheChipAndAddsADieToTheRight()
    {
        var canvas = SingleDieCanvasWithTwoParts();

        new CommandManager().ExecuteCommand(NewDie(canvas, out var command));

        canvas.Dies.IsAssemblyRoot.ShouldBeTrue();
        var dies = canvas.Dies.RootDies;
        dies.Count.ShouldBe(2);
        var chipDie = command.ConvertedChipDie!;
        DieAssembly.OutlineOf(chipDie).ShouldBe(new DieOutline(0, 0, ChipSize, ChipSize));
        chipDie.ChildComponents.Count.ShouldBe(2);
        chipDie.InternalPaths.Count.ShouldBe(1, "the wire between the two parts moves into the die");
        DieAssembly.OutlineOf(command.CreatedDie!).X.ShouldBe(ChipSize + DieAssembly.DefaultDieSpacingMicrometers);
        canvas.Connections.ShouldBeEmpty();
    }

    [Fact]
    public void NewDie_Undo_RestoresTheSingleDieDesignExactly()
    {
        var canvas = SingleDieCanvasWithTwoParts();
        var manager = new CommandManager();
        manager.ExecuteCommand(NewDie(canvas, out _));

        manager.Undo();

        canvas.Dies.IsAssemblyRoot.ShouldBeFalse();
        canvas.Components.Count.ShouldBe(2);
        canvas.Connections.Count.ShouldBe(1);
        canvas.Components.All(c => c.Component.ParentGroup == null).ShouldBeTrue();
        canvas.Dies.RootChipWidthMicrometers.ShouldBe(ChipSize);
        canvas.ChipMaxX.ShouldBe(ChipSize);
    }

    [Fact]
    public void NewDie_Redo_RecreatesTheAssembly()
    {
        var canvas = SingleDieCanvasWithTwoParts();
        var manager = new CommandManager();
        manager.ExecuteCommand(NewDie(canvas, out _));
        manager.Undo();

        manager.Redo();

        canvas.Dies.RootDies.Count.ShouldBe(2);
    }

    [Fact]
    public void NewDie_InAssembly_AddsOnlyTheNewDie()
    {
        var canvas = SingleDieCanvasWithTwoParts();
        var manager = new CommandManager();
        manager.ExecuteCommand(NewDie(canvas, out _));

        manager.ExecuteCommand(NewDie(canvas, out var second));

        second.ConvertedChipDie.ShouldBeNull();
        canvas.Dies.RootDies.Count.ShouldBe(3);
        canvas.Dies.RootDies.Select(DieAssembly.OutlineOf).Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public void AssemblyRoot_BoundsAreTheCarrierAroundAllDies()
    {
        var canvas = SingleDieCanvasWithTwoParts();
        new CommandManager().ExecuteCommand(NewDie(canvas, out var command));

        var carrier = DieAssembly.CarrierOutline(canvas.Dies.RootDies)!.Value;
        canvas.ChipMinX.ShouldBe(carrier.X - CAP.Avalonia.ViewModels.Canvas.Services.DieScopeService.CarrierMarginMicrometers);
        canvas.ChipMaxX.ShouldBe(carrier.Right + CAP.Avalonia.ViewModels.Canvas.Services.DieScopeService.CarrierMarginMicrometers);
        canvas.Dies.RootChipWidthMicrometers.ShouldBe(ChipSize, "the chip size setting survives for saving and undo");
    }

    [Fact]
    public void EnteringADie_BoundsPlacementByItsOutline_LeavingRestoresTheCarrier()
    {
        var canvas = SingleDieCanvasWithTwoParts();
        new CommandManager().ExecuteCommand(NewDie(canvas, out var command));
        var die = command.CreatedDie!;
        double carrierMaxX = canvas.ChipMaxX;

        canvas.EnterGroupEditMode(die);

        canvas.Dies.ActiveDie.ShouldBe(die);
        var outline = DieAssembly.OutlineOf(die);
        (canvas.ChipMinX, canvas.ChipMaxX).ShouldBe((outline.X, outline.Right));
        canvas.CanPlaceComponent(outline.X - 10, outline.Y + 10, 50, 20).ShouldBeFalse();

        canvas.ExitToRoot();

        canvas.Dies.ActiveDie.ShouldBeNull();
        canvas.ChipMaxX.ShouldBe(carrierMaxX);
    }

    [Fact]
    public void DroppingAnEdgeCouplerOnADie_JoinsTheDie_SnapsToTheEdge_AndExposesItsFacet()
    {
        var canvas = SingleDieCanvasWithTwoParts();
        var manager = new CommandManager();
        manager.ExecuteCommand(NewDie(canvas, out var newDie));
        var die = newDie.CreatedDie!;
        var outline = DieAssembly.OutlineOf(die);

        var place = PlaceComponentInDieCommand.TryCreate(canvas, die, EdgeCouplerTemplate(), outline.X + 80, outline.Y + 300)!;
        manager.ExecuteCommand(place);

        die.ChildComponents.ShouldContain(place.PlacedComponent);
        place.PlacedComponent.ParentGroup.ShouldBe(die);
        DieEdgeSnapper.FacetDistanceToEdge(place.PlacedComponent, outline).ShouldBe(0, 1e-6);
        var facet = EdgeCouplerFacet.FacetPinOf(place.PlacedComponent)!;
        die.ExternalPins.ShouldContain(p => p.InternalPin == facet);
        canvas.AllPins.ShouldContain(p => p.Pin == facet, "the facet is wireable from the assembly");

        manager.Undo();

        die.ChildComponents.ShouldNotContain(place.PlacedComponent);
        die.ExternalPins.ShouldNotContain(p => p.InternalPin == facet);
        canvas.AllPins.ShouldNotContain(p => p.Pin == facet);
    }

    [Fact]
    public void SnapEdgeCouplerCommand_UndoRestoresThePose()
    {
        var canvas = SingleDieCanvasWithTwoParts();
        new CommandManager().ExecuteCommand(NewDie(canvas, out var newDie));
        var die = newDie.CreatedDie!;
        var outline = DieAssembly.OutlineOf(die);
        canvas.EnterGroupEditMode(die);
        var coupler = canvas.AddComponent(EdgeCoupler("ec", outline.Right - 130, outline.Y + 200));

        var snap = SnapEdgeCouplerCommand.TryCreate(canvas, coupler)!;
        snap.Execute();

        EdgeCouplerFacet.FacetPinOf(coupler.Component)!.GetAbsoluteAngle().ShouldBe(0);
        coupler.X.ShouldBe(outline.Right - CouplerWidth, 1e-6);

        snap.Undo();

        coupler.Component.RotationDegrees.ShouldBe(0);
        coupler.X.ShouldBe(outline.Right - 130, 1e-6);
    }

    private static CreateDieCommand NewDie(DesignCanvasViewModel canvas, out CreateDieCommand command)
    {
        command = new CreateDieCommand(canvas,
            new DieSpec(DieAssembly.NextDieName(canvas.Dies.RootDies.Select(d => d.GroupName).Append("Die 1")), null, 1000, 1000),
            new DieSpec("Die 1", null, canvas.Dies.RootChipWidthMicrometers, canvas.Dies.RootChipHeightMicrometers));
        return command;
    }

    private static DesignCanvasViewModel SingleDieCanvasWithTwoParts()
    {
        var canvas = new DesignCanvasViewModel();
        canvas.SetRootChipSize(ChipSize, ChipSize);
        var a = canvas.AddComponent(Part("a", 100, 100));
        var b = canvas.AddComponent(Part("b", 400, 100));
        canvas.ConnectPins(a.Component.PhysicalPins[1], b.Component.PhysicalPins[0]);
        return canvas;
    }

    private static CAP.Avalonia.ViewModels.Library.ComponentTemplate EdgeCouplerTemplate() =>
        MultiProcessChipletJourneyDesign.TemplateFor(
            MultiProcessChipletJourneyDesign.LoadPdk(ChipletEdgeCouplerJourneyDesign.DemoPdkFile), "Edge Coupler");
}
