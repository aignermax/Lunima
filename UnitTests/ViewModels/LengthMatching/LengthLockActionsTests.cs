using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Routing.MeanderGeneration;
using Shouldly;
using UnitTests.Routing.LengthEqualization;
using Xunit;

namespace UnitTests.ViewModels.LengthMatching;

/// <summary>
/// The padlock's actions: a lock group is every route locked to the same length; moving a
/// component so far that its route cannot keep the length marks the lock as violated (the route
/// still follows the pins); "Re-equalize group" brings the whole group back to one length, and
/// releasing drops the lock of one route or of the whole group as one undo step.
/// </summary>
public class LengthLockActionsTests
{
    private const double FarMoveMicrometers = 900;

    private static async Task<(DesignCanvasViewModel Canvas, List<WaveguideConnectionViewModel> Connections,
        CommandManager Commands, LengthEqualizationViewModel Panel)> EqualizedFanInAsync(int count)
    {
        var (canvas, connections) = await FanInDesign.CreateAsync(count);
        var commands = new CommandManager();
        canvas.CommandManager = commands;
        var panel = new LengthEqualizationViewModel(canvas) { MaxRadiusText = "50" };
        foreach (var connection in connections)
            canvas.Selection.SelectedConnections.Add(connection);
        await panel.EqualizeCommand.ExecuteAsync(null);
        panel.HasErrorStatus.ShouldBeFalse(panel.StatusMessage);
        canvas.Selection.ClearConnectionSelection();
        return (canvas, connections, commands, panel);
    }

    [Fact]
    public async Task GroupOf_IsEveryRouteLockedToTheSameLength()
    {
        var (_, connections, _, panel) = await EqualizedFanInAsync(5);

        panel.Locks.GroupOf(connections[2]).Count.ShouldBe(5);
        panel.Locks.Describe(connections[2]).ShouldContain("5");
        LengthLockActions.IsViolated(connections[2]).ShouldBeFalse();
    }

    [Fact]
    public async Task FarMove_FollowsThePins_FlagsTheLock_AndReEqualizeFixesTheGroup()
    {
        var (canvas, connections, _, panel) = await EqualizedFanInAsync(5);
        var moved = connections[^1];
        var coupler = canvas.Components.Single(c => c.Component == moved.Connection.EndPin.ParentComponent);

        canvas.MoveComponent(coupler, 0, FarMoveMicrometers);
        await canvas.RecalculateRoutesAsync();

        var (endX, endY) = moved.Connection.EndPin.GetAbsolutePosition();
        moved.Connection.RoutedPath!.Segments[^1].EndPoint.Y.ShouldBe(endY, 1e-3);
        moved.Connection.RoutedPath!.Segments[^1].EndPoint.X.ShouldBe(endX, 1e-3);
        LengthLockActions.IsViolated(moved).ShouldBeTrue();

        await panel.Locks.ReEqualizeAsync(moved);

        connections.ShouldAllBe(c => ConnectionLengthMatcher.MeetsTarget(c.Connection) && c.Connection.TargetLengthMicrometers != null);
        connections.Select(c => c.Connection.TargetLengthMicrometers).Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Release_OneRoute_OrTheWholeGroup_IsOneUndoStepEach()
    {
        var (_, connections, commands, panel) = await EqualizedFanInAsync(4);
        int before = commands.UndoCount;

        panel.Locks.Release(new[] { connections[0] }).ShouldBe(1);
        connections[0].Connection.TargetLengthMicrometers.ShouldBeNull();
        panel.Locks.GroupOf(connections[1]).Count.ShouldBe(3);

        panel.Locks.Release(panel.Locks.GroupOf(connections[1])).ShouldBe(3);
        connections.ShouldAllBe(c => c.Connection.TargetLengthMicrometers == null);
        commands.UndoCount.ShouldBe(before + 2);

        commands.Undo();
        panel.Locks.GroupOf(connections[1]).Count.ShouldBe(3);
    }
}
