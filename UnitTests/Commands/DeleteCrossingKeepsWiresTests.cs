using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using CAP_Core.Routing.CrossingInsertion.ManualCrossing;
using Shouldly;
using UnitTests.Routing.CrossingInsertion;
using UnitTests.Routing.CrossingInsertion.CrossingMove;
using Xunit;

namespace UnitTests.Commands;

/// <summary>
/// Deleting a user-placed crossing hands its wires back, like deleting a routing-placed one
/// (covered on a shipped example by <c>ChainCrossingMaintenanceTests</c>): two wires through
/// it merge and route again, a Cut-tool split merges back into one wire with its straight
/// geometry, and undo restores the crossing with its pieces.
/// </summary>
public class DeleteCrossingKeepsWiresTests
{
    [Fact]
    public async Task DeletingAUserPlacedCrossing_MergesBothWires_AndUndoRestoresThePieces()
    {
        var scene = CrossedWiresScene.Build();
        var commands = new CommandManager();
        var crossingVm = scene.Canvas.Components.Single(vm => vm.Component == scene.Crossing);

        commands.ExecuteCommand(new DeleteComponentCommand(scene.Canvas, crossingVm));
        await scene.Canvas.RecalculateRoutesAsync();

        var wires = scene.Canvas.ConnectionManager.Connections;
        wires.Count.ShouldBe(2, "four pieces merge into two wires instead of being deleted");
        wires.ShouldContain(c => c.StartPin == scene.West.StartPin && c.EndPin == scene.East.EndPin);
        wires.ShouldContain(c => c.StartPin == scene.North.StartPin && c.EndPin == scene.South.EndPin);
        wires.ShouldAllBe(c => c.IsPathValid);
        scene.Canvas.Connections.Select(vm => vm.Connection).ShouldBe(wires, ignoreOrder: true);

        commands.Undo();

        scene.Canvas.Components.ShouldContain(vm => vm.Component == scene.Crossing);
        scene.Canvas.ConnectionManager.Connections.ShouldBe(scene.Pieces, ignoreOrder: true);
    }

    [Fact]
    public async Task DeletingACutToolCrossing_MergesTheWireBack_WithItsStraightGeometry()
    {
        var canvas = new CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel();
        canvas.InitializeAStarRouting(0, 0, CrossedWiresScene.ChipSize, CrossedWiresScene.ChipSize);
        var left = CrossingTestCircuit.CreateTerminal("left", 0, 95, pinAngleDegrees: 0);
        var right = CrossingTestCircuit.CreateTerminal("right", 380, 95, pinAngleDegrees: 180);
        canvas.AddComponent(left.Component, "Terminal");
        canvas.AddComponent(right.Component, "Terminal");
        var route = new RoutedPath();
        route.Segments.Add(new StraightSegment(10, 100, 380, 100, 0));
        var wire = canvas.ConnectPinsWithCachedRoute(left.PhysicalPin, right.PhysicalPin, route)!.Connection;
        var crossing = new CrossingComponentInstance(CrossingTestCircuit.CreateCrossingComponent(), "Crossing 4-Port", "Test PDK");
        var candidate = new ManualCrossingCandidate(wire, (StraightSegment)route.Segments[0], null, (200, 100), true, (1, 0));
        var commands = new CommandManager();
        var cut = new InsertManualCrossingCommand(canvas, candidate, crossing);
        commands.ExecuteCommand(cut);
        await cut.RerouteTask;
        var halves = canvas.ConnectionManager.Connections.ToList();
        halves.Count.ShouldBe(2);

        commands.ExecuteCommand(new DeleteComponentCommand(canvas, canvas.Components.Single(vm => vm.Component == crossing.Component)));

        var merged = canvas.ConnectionManager.Connections.ShouldHaveSingleItem();
        merged.StartPin.ShouldBe(left.PhysicalPin);
        merged.EndPin.ShouldBe(right.PhysicalPin);
        merged.GetPathSegments()[0].StartPoint.ShouldBe((10.0, 100.0));
        merged.GetPathSegments()[^1].EndPoint.ShouldBe((380.0, 100.0));
        merged.GetPathSegments().ShouldAllBe(s => s is StraightSegment && Math.Abs(s.StartPoint.Y - 100) < 1e-9,
            "a single wire through the crossing keeps its straight geometry");

        commands.Undo();
        canvas.ConnectionManager.Connections.ShouldBe(halves, ignoreOrder: true);
        canvas.Components.ShouldContain(vm => vm.Component == crossing.Component);
    }
}
