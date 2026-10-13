using CAP.Avalonia.Commands;
using CAP_Core.Routing.CrossingInsertion;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.CrossingInsertion;

/// <summary>
/// Crossings the router placed are temporary whether or not adaptive crossing insertion is
/// on: when a wire through them goes stale its chain is taken out, crossings the user placed
/// are never touched, and re-routing the stale wires never re-routes the whole design.
/// Checked on the shipped 2-bit counter.
/// </summary>
public class TemporaryCrossingTests
{
    [Fact]
    public async Task AdaptiveOff_MovingAWireEnd_DissolvesItsStaleChains()
    {
        var scenario = await CounterCrossingScenario.LoadAsync(adaptive: false);
        var group = scenario.GroupToMove();
        var stale = scenario.CrossingsDockedTo(group);
        stale.ShouldNotBeEmpty();
        stale.ShouldAllBe(c => c.IsInsertedCrossing);

        await scenario.MoveAndRouteAsync(group);

        foreach (var crossing in stale)
        {
            scenario.Canvas.Components.ShouldNotContain(vm => vm.Component == crossing,
                "a router-placed crossing of a wire whose end moved is taken out even with adaptive insertion off");
            scenario.Pieces(crossing).ShouldBeEmpty();
        }
        scenario.Canvas.Connections.Select(vm => vm.Connection)
            .ShouldBe(scenario.Manager.Connections, ignoreOrder: true);
        scenario.Manager.LastRoutingPassTimings!.OrderingAttempts.ShouldBe(0,
            "re-routing the dissolved wires must not re-route the whole design");
    }

    [Fact]
    public async Task AdaptiveOff_UserPlacedCrossings_StayWhenTheirWireEndMoves()
    {
        var scenario = await CounterCrossingScenario.LoadAsync(adaptive: false);
        var group = scenario.GroupToMove();
        var userPlaced = scenario.CrossingsDockedTo(group);
        foreach (var crossing in userPlaced)
            crossing.IsInsertedCrossing = false;

        await scenario.MoveAndRouteAsync(group);

        foreach (var crossing in userPlaced)
        {
            scenario.Canvas.Components.ShouldContain(vm => vm.Component == crossing,
                "a crossing the user placed is never dissolved");
            scenario.Pieces(crossing).Count.ShouldBe(4);
        }
    }

    [Fact]
    public async Task UnchangedDesign_KeepsEveryCrossing_AndRunsNoCascade()
    {
        var scenario = await CounterCrossingScenario.LoadAsync(adaptive: false);
        var before = scenario.Crossings();
        int blockedBefore = scenario.BlockedCount();

        await scenario.Canvas.RecalculateRoutesAsync();

        scenario.Crossings().ShouldBe(before, ignoreOrder: true);
        scenario.BlockedCount().ShouldBe(blockedBefore, "a recalculation without any change must not degrade the design");
        scenario.Manager.LastRoutingPassTimings!.OrderingAttempts.ShouldBe(0);
    }

    [Fact]
    public async Task DeletingAChainPiece_TakesTheChainOut_AndUndoRestoresAWholeWire()
    {
        var scenario = await CounterCrossingScenario.LoadAsync(adaptive: false);
        var crossing = scenario.Crossings().First();
        var piece = scenario.Pieces(crossing).First();
        var pieceVm = scenario.Canvas.Connections.Single(vm => vm.Connection == piece);
        int crossingsBefore = scenario.Crossings().Count;
        var commands = new CommandManager();

        commands.ExecuteCommand(new DeleteConnectionCommand(scenario.Canvas, pieceVm));

        scenario.Canvas.Components.ShouldNotContain(vm => vm.Component == crossing);
        scenario.Crossings().Count.ShouldBeLessThan(crossingsBefore);
        scenario.Manager.Connections.ShouldNotContain(c => DocksOnRemovedCrossing(scenario, c), "no orphaned pieces");

        commands.Undo();

        scenario.Manager.Connections.Except(scenario.Canvas.Connections.Select(vm => vm.Connection))
            .ShouldBeEmpty("the canvas shows every restored wire");
        scenario.Manager.Connections.ShouldNotContain(c => DocksOnRemovedCrossing(scenario, c),
            "undo never restores a piece of a crossing that is gone");
    }

    private static bool DocksOnRemovedCrossing(CounterCrossingScenario scenario, CAP_Core.Components.Connections.WaveguideConnection connection) =>
        new[] { connection.StartPin.ParentComponent, connection.EndPin.ParentComponent }
            .Any(c => CrossingComponentCatalog.IsCrossing(c) && !scenario.Canvas.Components.Any(vm => vm.Component == c));
}
