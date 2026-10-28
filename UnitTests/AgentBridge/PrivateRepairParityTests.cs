using CAP.Avalonia.Services.AgentBridge.Tools.Repair;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;
using Shouldly;
using Xunit;
using static UnitTests.AgentBridge.RepairTestBench;

namespace UnitTests.AgentBridge;

/// <summary>Every generated repair family uses the same actual commands and joint router in trial and apply.</summary>
[Trait("Category", "Heavy")]
public sealed class PrivateRepairParityTests
{
    [Theory]
    [InlineData("cut_through")]
    [InlineData("reorder_neighbour")]
    [InlineData("move_crossing")]
    [InlineData("shift_segment")]
    [InlineData("nudge_component")]
    public async Task PrivateTrial_AndActualApply_AgreeWithoutChangingLiveHistoryDuringTrial(string family)
    {
        var bench = await RepairParityScene.CreateAsync(family);
        try
        {
            var session = bench.Session;
            var target = session.FindConnection(bench.FirstBlockedId())!.Connection;
            var candidate = new RepairCandidateGenerator(session, target).Generate()
                .FirstOrDefault(value => value.Kind == family).ShouldNotBeNull("the fixture must exercise this repair family");
            var baseline = RoutingMetrics.Measure(session);
            var components = bench.Canvas.Components.Select(view => (view.Component, view.X, view.Y)).ToArray();
            var connections = bench.Canvas.ConnectionManager.Connections.ToArray();
            var paths = connections.Select(wire => wire.RoutedPath).ToArray();
            var undo = bench.Commands.UndoCount;
            var redo = bench.Commands.RedoCount;
            TrialOutcome outcome;
            await using (var permission = new RepairTrialPermission(session, null))
            {
                var sandbox = new RepairSandbox(session, target, permission, null);
                outcome = await sandbox.TryAsync(candidate, baseline);
                outcome.Applicable.ShouldBeTrue(outcome.Note);
                permission.EnsureCurrent();
            }
            bench.Canvas.Components.Select(view => view.Component).ShouldBe(components.Select(value => value.Component));
            foreach (var (component, x, y) in components)
            {
                component.PhysicalX.ShouldBe(x);
                component.PhysicalY.ShouldBe(y);
            }
            bench.Canvas.ConnectionManager.Connections.ShouldBe(connections);
            for (var index = 0; index < connections.Length; index++) connections[index].RoutedPath.ShouldBeSameAs(paths[index]);
            bench.Commands.UndoCount.ShouldBe(undo);
            bench.Commands.RedoCount.ShouldBe(redo);
            session.Suggestions = new(target.Id.ToString(), RepairSuggestionSet.ComputeStamp(session), [new("s1", outcome)]);
            var result = Body(await new ApplySuggestionTool(session).ExecuteAsync(Args("{\"id\":\"s1\",\"allow_worse\":true}")));
            result.GetProperty("blocked_after").GetInt32().ShouldBe(outcome.BlockedAfter);
            result.GetProperty("crossings_added").GetInt32().ShouldBe(outcome.CrossingsAdded);
            var stillBlocked = bench.Canvas.Connections.Any(view => view.Connection.IsBlockedFallback &&
                (view.Connection.StartPin.PinId == target.StartPin.PinId || view.Connection.EndPin.PinId == target.StartPin.PinId ||
                 view.Connection.StartPin.PinId == target.EndPin.PinId || view.Connection.EndPin.PinId == target.EndPin.PinId));
            (!stillBlocked).ShouldBe(outcome.TargetFixed);
            (bench.Commands.UndoCount - undo).ShouldBe(result.GetProperty("undo_steps").GetInt32());
        }
        finally
        {
            await bench.DisposeAsync();
        }
    }
}
