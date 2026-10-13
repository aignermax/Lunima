using CAP.Avalonia.Commands;
using CAP_Core.Routing.CrossingInsertion;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.CrossingInsertion;

/// <summary>
/// "Clean up crossings" removes every router-placed crossing, merges the wires through them
/// pin to pin for a fresh route and leaves crossings the user placed alone; undo puts the
/// crossings, their pieces and their routes back.
/// </summary>
public class CleanUpCrossingsCommandTests
{
    [Fact]
    public async Task CleanUp_RemovesRouterCrossings_KeepsUserCrossings_AndUndoRestoresThem()
    {
        var scenario = await CounterCrossingScenario.LoadAsync(adaptive: false);
        var crossings = scenario.Crossings();
        var userPlaced = crossings[^1];
        userPlaced.IsInsertedCrossing = false;
        var routerPlaced = crossings.Where(c => c != userPlaced).ToList();
        var connectionsBefore = scenario.Manager.Connections.ToList();
        var commands = new CommandManager();
        scenario.Canvas.CommandManager = commands;

        scenario.Canvas.CleanUpCrossingsCommand.Execute(null);

        scenario.Canvas.Components.ShouldNotContain(vm => routerPlaced.Contains(vm.Component));
        scenario.Canvas.Components.ShouldContain(vm => vm.Component == userPlaced);
        scenario.Manager.Connections.ShouldNotContain(c => ChainCrossingMaintenance.DocksOnTemporaryCrossing(c));
        scenario.Canvas.Connections.Select(vm => vm.Connection).ShouldBe(scenario.Manager.Connections, ignoreOrder: true);
        commands.CanUndo.ShouldBeTrue();
        await scenario.Canvas.RecalculateRoutesAsync();

        commands.Undo();
        await scenario.Canvas.RecalculateRoutesAsync();

        foreach (var crossing in routerPlaced)
        {
            scenario.Canvas.Components.ShouldContain(vm => vm.Component == crossing);
            scenario.Pieces(crossing).Count.ShouldBe(4);
        }
        scenario.Manager.Connections.ShouldBe(connectionsBefore, ignoreOrder: true);
        scenario.Canvas.Connections.Select(vm => vm.Connection).ShouldBe(scenario.Manager.Connections, ignoreOrder: true);
    }

    [Fact]
    public async Task CleanUp_WithAdaptiveInsertionOff_LeavesNoRouterCrossingAfterRouting()
    {
        var scenario = await CounterCrossingScenario.LoadAsync(adaptive: false);

        scenario.Canvas.CleanUpCrossingsCommand.Execute(null);
        await scenario.Canvas.RecalculateRoutesAsync();

        scenario.Crossings().ShouldBeEmpty("without adaptive insertion no crossing is placed again");
        scenario.Manager.Connections.ShouldAllBe(c => c.RoutedPath != null, "every merged wire was routed again");
    }
}
