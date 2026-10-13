using CAP.Avalonia.Commands;
using CAP.Avalonia.Controls.Canvas.CutTool;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_Core.Routing.CrossingInsertion;
using CAP_Core.Routing.CrossingInsertion.CrossingMove;
using CAP_Core.Routing.InterconnectRouting.SegmentShift;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.CrossingInsertion;

/// <summary>
/// Editing the pieces of routing-placed crossings on the shipped 2-bit counter: a piece can be
/// cut like any wire and segment-shifted, and a crossing slides along its wire — none of it
/// takes out the chain the piece belongs to, and undo restores the design.
/// </summary>
public class CrossingPieceEditingTests
{
    /// <summary>Shortest straight (µm) the cut tests try, leaving room for the crossing's ports.</summary>
    private const double MinCutStraightMicrometers = 40;

    private static readonly double[] MoveDistances = { 5, -5, 10, -10, 20, -20 };

    [Fact]
    public async Task CuttingARouterCrossingPiece_KeepsItsChain_AndUndoRestoresThePiece()
    {
        var scenario = await LoadSettledAsync();
        var templates = TestPdkLoader.LoadAllTemplates();
        var (piece, command) = FindCuttablePiece(scenario, templates);
        var routerCrossing = RouterCrossingOf(piece);
        var crossingsBefore = scenario.Crossings().ToHashSet();
        var pieceGeometry = Geometry(piece);
        CutToolCandidateComputer.CollectEligibleConnections(scenario.Canvas).ShouldContain(piece,
            "a piece of a router-placed crossing is offered to the Cut tool");

        scenario.Commands.ExecuteCommand(command);
        await command.RerouteTask;

        command.Succeeded.ShouldBeTrue();
        scenario.Crossings().ShouldContain(command.Crossing);
        command.Crossing.IsInsertedCrossing.ShouldBeFalse();
        scenario.Pieces(command.Crossing).Count.ShouldBe(2);
        crossingsBefore.ShouldAllBe(c => scenario.Crossings().Contains(c),
            "cutting a piece must not take out the chain it belongs to");
        routerCrossing.IsInsertedCrossing.ShouldBeTrue();
        scenario.Pieces(routerCrossing).Count.ShouldBe(4);
        var half = scenario.Pieces(routerCrossing).Single(p => scenario.Pieces(command.Crossing).Contains(p));
        half.IsPathValid.ShouldBeTrue();
        scenario.Manager.Connections.ShouldNotContain(piece);
        scenario.Manager.LastRoutingPassTimings!.OrderingAttempts.ShouldBe(0,
            $"a cut keeps the halves' geometry and re-routes nothing ({scenario.Manager.LastRoutingPassTimings})");

        scenario.Commands.Undo();
        await command.RerouteTask;

        scenario.Crossings().ShouldNotContain(command.Crossing);
        scenario.Manager.Connections.ShouldContain(piece);
        Geometry(piece).ShouldBe(pieceGeometry, "undo restores the cut piece exactly");
        scenario.Pieces(routerCrossing).Count.ShouldBe(4);
    }

    [Fact]
    public async Task ShiftingASegmentOfARouterCrossingPiece_KeepsItsChain()
    {
        var scenario = await LoadSettledAsync();
        var crossingsBefore = scenario.Crossings().ToHashSet();
        var (vm, index, offset) = FindShiftableSegment(scenario);

        scenario.Commands.ExecuteCommand(new SegmentShiftCommand(vm, index, 0, offset));
        await scenario.Canvas.RecalculateRoutesAsync();

        scenario.Manager.Connections.ShouldContain(vm.Connection, "the shifted piece is kept, not dissolved");
        vm.Connection.StraightShiftOffsets[index].ShouldBe(offset);
        scenario.Crossings().ShouldBe(crossingsBefore, ignoreOrder: true);
    }

    [Fact]
    public async Task MovingARouterCrossingAlongItsWire_KeepsTheOtherChains_AndUndoRestoresIt()
    {
        var scenario = await LoadSettledAsync();
        var (crossing, axis, distance) = FindMovableCrossing(scenario);
        var crossingsBefore = scenario.Crossings().ToHashSet();
        var pieces = scenario.Pieces(crossing);
        var before = pieces.ToDictionary(p => p, Geometry);
        double startX = crossing.PhysicalX, startY = crossing.PhysicalY;
        var command = new MoveCrossingAlongAxisCommand(scenario.Canvas, crossing, axis, distance);

        scenario.Commands.ExecuteCommand(command);
        await command.RerouteTask;

        command.Succeeded.ShouldBeTrue(command.Error);
        crossing.IsInsertedCrossing.ShouldBeFalse("the user moved it: the router never takes it out again");
        scenario.Crossings().ShouldBe(crossingsBefore, ignoreOrder: true);
        foreach (var patched in command.LastPlan!.Patched)
            patched.Piece.GetPathSegments().Count.ShouldBe(before[patched.Piece].Count, "patched, not re-routed");

        scenario.Commands.Undo();
        await command.RerouteTask;

        crossing.PhysicalX.ShouldBe(startX, 1e-9);
        crossing.PhysicalY.ShouldBe(startY, 1e-9);
        crossing.IsInsertedCrossing.ShouldBeTrue();
        foreach (var piece in pieces)
            Geometry(piece).ShouldBe(before[piece], "undo restores every piece exactly");
    }

    /// <summary>
    /// The counter after one more routing pass: the pin-lead collapse may still shorten a lead
    /// the load pass left, so "before" is captured on the settled design.
    /// </summary>
    private static async Task<CounterCrossingScenario> LoadSettledAsync()
    {
        var scenario = await CounterCrossingScenario.LoadAsync(adaptive: false);
        await scenario.Canvas.RecalculateRoutesAsync();
        return scenario;
    }

    private static (WaveguideConnection Piece, InsertManualCrossingCommand Command) FindCuttablePiece(
        CounterCrossingScenario scenario, IReadOnlyList<CAP.Avalonia.ViewModels.Library.ComponentTemplate> templates)
    {
        foreach (var piece in scenario.Manager.Connections.Where(ChainCrossingMaintenance.DocksOnTemporaryCrossing).ToList())
        {
            foreach (var straight in piece.GetPathSegments().OfType<StraightSegment>().Where(s => s.LengthMicrometers >= MinCutStraightMicrometers))
            {
                var mid = ((straight.StartPoint.X + straight.EndPoint.X) / 2, (straight.StartPoint.Y + straight.EndPoint.Y) / 2);
                var command = CutAtPoint.CreateCommand(scenario.Canvas, templates, piece, mid.Item1, mid.Item2, out _);
                if (command != null)
                    return (piece, command);
            }
        }
        throw new InvalidOperationException("The counter has no router-crossing piece with room for a cut.");
    }

    private static (CAP.Avalonia.ViewModels.Canvas.WaveguideConnectionViewModel Vm, int Index, double Offset) FindShiftableSegment(
        CounterCrossingScenario scenario)
    {
        foreach (var vm in scenario.Canvas.Connections.Where(c => ChainCrossingMaintenance.DocksOnTemporaryCrossing(c.Connection)))
        {
            foreach (var handle in SegmentShiftGeometry.GetHandles(vm.Connection.GetPathSegments()))
            {
                var probe = vm.Connection.RoutedPath!.DeepCopy();
                int segmentIndex = vm.Connection.GetPathSegments().ToList().FindIndex(s =>
                    s is StraightSegment && (s.StartPoint.X + s.EndPoint.X) / 2 == handle.Midpoint.X
                    && (s.StartPoint.Y + s.EndPoint.Y) / 2 == handle.Midpoint.Y);
                foreach (double offset in new[] { 2.0, -2.0 })
                {
                    if (segmentIndex >= 0 && SegmentShiftEditor.TryShiftStraightSegment(probe.Segments, segmentIndex, offset, out _))
                        return (vm, handle.StraightIndex, offset);
                }
            }
        }
        throw new InvalidOperationException("The counter has no router-crossing piece with a shiftable segment.");
    }

    private static (Component Crossing, CrossingAxis Axis, double Distance) FindMovableCrossing(CounterCrossingScenario scenario)
    {
        foreach (var crossing in scenario.Crossings().Where(c => c.IsInsertedCrossing))
        foreach (var axis in new[] { CrossingAxis.Horizontal, CrossingAxis.Vertical })
        foreach (double distance in MoveDistances)
        {
            if (MoveCrossingAlongAxisCommand.Validate(scenario.Canvas, crossing, axis, distance) != null) continue;
            var plan = CrossingAxisMovePlanner.Plan(crossing, scenario.Manager.Connections, axis, distance);
            if (plan.Patched.Count == 2)
                return (crossing, axis, distance);
        }
        throw new InvalidOperationException("The counter has no router crossing that can slide along its wire.");
    }

    private static Component RouterCrossingOf(WaveguideConnection piece) =>
        ChainCrossingMaintenance.IsTemporaryCrossing(piece.StartPin.ParentComponent)
            ? piece.StartPin.ParentComponent
            : piece.EndPin.ParentComponent;

    private static List<((double X, double Y) Start, (double X, double Y) End)> Geometry(WaveguideConnection piece) =>
        piece.GetPathSegments().Select(s => (s.StartPoint, s.EndPoint)).ToList();
}
