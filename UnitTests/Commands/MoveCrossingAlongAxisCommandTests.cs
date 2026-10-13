using CAP.Avalonia.Commands;
using CAP_Core.Routing.CrossingInsertion.CrossingMove;
using Shouldly;
using UnitTests.Routing.CrossingInsertion;
using UnitTests.Routing.CrossingInsertion.CrossingMove;
using Xunit;

namespace UnitTests.Commands;

/// <summary>
/// <see cref="MoveCrossingAlongAxisCommand"/>: the crossing slides along one wire, that wire
/// keeps its geometry apart from the stretched/shrunk end straights, the wire across it routes
/// again, and undo/redo restore everything exactly.
/// </summary>
public class MoveCrossingAlongAxisCommandTests
{
    private const double Shift = 40;
    private static readonly double Half = CrossingTestCircuit.CrossingEdgeMicrometers / 2.0;

    [Fact]
    public async Task Execute_MovesCrossing_PatchesAlongPieces_ReroutesAcrossPieces()
    {
        var scene = CrossedWiresScene.Build();
        double startX = scene.Crossing.PhysicalX;
        var commands = new CommandManager();
        var command = new MoveCrossingAlongAxisCommand(scene.Canvas, scene.Crossing, CrossingAxis.Horizontal, Shift);

        commands.ExecuteCommand(command);
        await command.RerouteTask;

        command.Succeeded.ShouldBeTrue(command.Error);
        scene.Crossing.PhysicalX.ShouldBe(startX + Shift, 1e-9);
        scene.Canvas.Components.Single(vm => vm.Component == scene.Crossing).X.ShouldBe(startX + Shift, 1e-9);
        command.LastPlan!.Rerouted.ShouldBe(new[] { scene.North, scene.South }, ignoreOrder: true);

        var west = scene.West.GetPathSegments().ShouldHaveSingleItem("the along-axis piece is patched, not re-routed");
        west.EndPoint.X.ShouldBe(CrossedWiresScene.Center - Half + Shift, 1e-9);
        var east = scene.East.GetPathSegments().ShouldHaveSingleItem();
        east.StartPoint.X.ShouldBe(CrossedWiresScene.Center + Half + Shift, 1e-9);

        foreach (var piece in new[] { scene.North, scene.South })
        {
            piece.IsPathValid.ShouldBeTrue("the across-axis pieces route again to the moved ports");
            piece.IsBlockedFallback.ShouldBeFalse();
        }
        var (northPortX, northPortY) = CrossedWiresScene.Port(scene.Crossing, "port 3").GetAbsolutePosition();
        scene.North.GetPathSegments()[^1].EndPoint.X.ShouldBe(northPortX, 0.5);
        scene.North.GetPathSegments()[^1].EndPoint.Y.ShouldBe(northPortY, 0.5);
    }

    [Fact]
    public async Task UndoRedo_RestoresAndRepeatsTheMoveExactly()
    {
        var scene = CrossedWiresScene.Build();
        double startX = scene.Crossing.PhysicalX;
        var before = scene.Pieces.ToDictionary(p => p, CrossedWiresScene.Geometry);
        var commands = new CommandManager();
        var command = new MoveCrossingAlongAxisCommand(scene.Canvas, scene.Crossing, CrossingAxis.Horizontal, Shift);
        commands.ExecuteCommand(command);
        await command.RerouteTask;
        var after = CrossedWiresScene.Geometry(scene.West);

        commands.Undo();
        await command.RerouteTask;

        scene.Crossing.PhysicalX.ShouldBe(startX, 1e-9);
        foreach (var piece in scene.Pieces)
            CrossedWiresScene.Geometry(piece).ShouldBe(before[piece], "undo restores every piece's geometry");
        scene.Canvas.ConnectionManager.Connections.Count.ShouldBe(4);

        commands.Redo();
        await command.RerouteTask;

        scene.Crossing.PhysicalX.ShouldBe(startX + Shift, 1e-9);
        CrossedWiresScene.Geometry(scene.West).ShouldBe(after);
    }

    [Fact]
    public async Task MovingARouterPlacedCrossing_MakesItUserOwned_UntilUndo()
    {
        var scene = CrossedWiresScene.Build(routerPlaced: true);
        var commands = new CommandManager();
        var command = new MoveCrossingAlongAxisCommand(scene.Canvas, scene.Crossing, CrossingAxis.Vertical, Shift);

        commands.ExecuteCommand(command);
        await command.RerouteTask;
        scene.Crossing.IsInsertedCrossing.ShouldBeFalse("a deliberately moved crossing is never dissolved by the router again");

        commands.Undo();
        await command.RerouteTask;
        scene.Crossing.IsInsertedCrossing.ShouldBeTrue();
    }

    [Fact]
    public void MoveOntoAnotherComponent_IsRefused_AndChangesNothing()
    {
        var scene = CrossedWiresScene.Build();
        double startX = scene.Crossing.PhysicalX;
        double towardsRightTerminal = 550 - CrossedWiresScene.Center;

        MoveCrossingAlongAxisCommand.Validate(scene.Canvas, scene.Crossing, CrossingAxis.Horizontal, towardsRightTerminal)
            .ShouldNotBeNull();
        var command = new MoveCrossingAlongAxisCommand(scene.Canvas, scene.Crossing, CrossingAxis.Horizontal, towardsRightTerminal);
        command.Execute();

        command.Succeeded.ShouldBeFalse();
        command.Error.ShouldNotBeNull();
        scene.Crossing.PhysicalX.ShouldBe(startX);
        command.Undo();
        scene.Crossing.PhysicalX.ShouldBe(startX, "undoing a refused move is a no-op");
    }
}
