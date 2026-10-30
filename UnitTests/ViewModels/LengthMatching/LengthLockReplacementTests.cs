using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;
using UnitTests.Routing.LengthEqualization;
using Xunit;

namespace UnitTests.ViewModels.LengthMatching;

/// <summary>
/// Equalizing again replaces the old locks of the selected routes: the new radius applies to every
/// route, not only to the ones that were unlocked, and undo brings the old locks back in one step.
/// A route without room keeps its route but carries the target as an unmet (warning) lock.
/// </summary>
public class LengthLockReplacementTests
{
    private const int RouteCount = 4;

    private static async Task<(DesignCanvasViewModel Canvas, List<WaveguideConnectionViewModel> Connections,
        CommandManager Commands, LengthEqualizationViewModel Panel)> SelectedFanInAsync()
    {
        var (canvas, connections) = await FanInDesign.CreateAsync(RouteCount);
        var commands = new CommandManager();
        canvas.CommandManager = commands;
        var panel = new LengthEqualizationViewModel(canvas);
        foreach (var connection in connections)
            canvas.Selection.SelectedConnections.Add(connection);
        return (canvas, connections, commands, panel);
    }

    private static async Task EqualizeAsync(LengthEqualizationViewModel panel, string radius)
    {
        panel.MaxRadiusText = radius;
        panel.MinRadiusText = radius;
        await panel.EqualizeCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task EqualizeAgain_AppliesTheNewRadiusToEveryRoute_AndUndoRestoresTheOldLocks()
    {
        var (_, connections, commands, panel) = await SelectedFanInAsync();
        await EqualizeAsync(panel, "50");
        panel.HasErrorStatus.ShouldBeFalse(panel.StatusMessage);
        connections.ShouldAllBe(c => c.Connection.BendRadiusMicrometers == 50);
        int undoSteps = commands.UndoCount;

        await EqualizeAsync(panel, "30");

        panel.HasErrorStatus.ShouldBeFalse(panel.StatusMessage);
        connections.ShouldAllBe(c => c.Connection.BendRadiusMicrometers == 30 && c.Connection.IsRouteFrozen);
        connections.Select(c => c.Connection.TargetLengthMicrometers).Distinct().Count().ShouldBe(1);
        commands.UndoCount.ShouldBe(undoSteps + 1, "the replacement is one undo step");

        commands.Undo();
        connections.ShouldAllBe(c => c.Connection.BendRadiusMicrometers == 50 && c.Connection.IsRouteFrozen);
    }

    [Fact]
    public async Task RoutesWithoutRoom_KeepTheirRoute_ButShowAnUnmetLock()
    {
        var (_, connections, _, panel) = await SelectedFanInAsync();
        var lengths = connections.Select(c => c.Connection.PathLengthMicrometers).ToList();

        await EqualizeAsync(panel, "5000");

        panel.HasErrorStatus.ShouldBeTrue(panel.StatusMessage);
        var failed = connections.Where(c => !c.Connection.IsRouteFrozen).ToList();
        failed.ShouldNotBeEmpty("a 5 mm loop has no room next to the fan-in");
        foreach (var connection in failed)
        {
            connection.Connection.TargetLengthMicrometers.ShouldNotBeNull();
            LengthLockActions.IsViolated(connection).ShouldBeTrue();
            connection.Connection.PathLengthMicrometers.ShouldBe(lengths[connections.IndexOf(connection)], 1e-6);
        }
    }
}
