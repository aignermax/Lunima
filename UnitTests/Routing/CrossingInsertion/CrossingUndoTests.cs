using Shouldly;
using Xunit;

namespace UnitTests.Routing.CrossingInsertion;

/// <summary>
/// Undo never lies: the re-route after a move takes out the router-placed crossings whose
/// wires went stale (and may place new ones), and undoing the move must bring the design
/// back exactly — same crossings, connections, blocked wires and components as before it.
/// A redo makes the same changes again.
/// </summary>
public class CrossingUndoTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Slow")]
    [Trait("Category", "Heavy")]
    public async Task MoveUndoRedo_RestoresTheExactCrossingState(bool adaptive)
    {
        var scenario = await CounterCrossingScenario.LoadAsync(adaptive);
        var group = scenario.GroupToMove();
        var loaded = scenario.State();
        var crossingsAtLoad = scenario.Crossings();

        await scenario.MoveWithCommandAsync(group);
        var moved = scenario.State();
        moved.Crossings.ShouldBeLessThan(loaded.Crossings, "the move makes chains stale (fixture sanity)");

        scenario.Commands.Undo();
        await scenario.Canvas.RecalculateRoutesAsync();

        AssertSame(scenario.State(), loaded, "undo restores the state right after load");
        scenario.Crossings().ShouldBe(crossingsAtLoad, ignoreOrder: true, "the very same crossings come back");
        scenario.Canvas.Connections.Select(vm => vm.Connection)
            .ShouldBe(scenario.Manager.Connections, ignoreOrder: true);

        scenario.Commands.Redo();
        await scenario.Canvas.RecalculateRoutesAsync();

        AssertSame(scenario.State(), moved, "redo repeats the move and its re-route");
    }

    private static void AssertSame(
        (int Crossings, int Connections, int Blocked, HashSet<CAP_Core.Components.Core.Component> Components) actual,
        (int Crossings, int Connections, int Blocked, HashSet<CAP_Core.Components.Core.Component> Components) expected,
        string because)
    {
        actual.Crossings.ShouldBe(expected.Crossings, because + " (crossings)");
        actual.Connections.ShouldBe(expected.Connections, because + " (connections)");
        actual.Blocked.ShouldBe(expected.Blocked, because + " (blocked)");
        actual.Components.SetEquals(expected.Components).ShouldBeTrue(because + " (components)");
    }
}
