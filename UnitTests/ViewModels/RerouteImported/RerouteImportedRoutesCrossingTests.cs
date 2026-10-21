using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP.Avalonia.ViewModels.Canvas.RerouteImported;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;
using UnitTests.Routing.CrossingInsertion;
using Xunit;

namespace UnitTests.ViewModels.RerouteImported;

public partial class RerouteImportedRoutesViewModelTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RerouteCrossing_UndoRedoRestoresExactTopology(bool grouped, bool editGroup)
    {
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting(0, 0, 400, 400);
        canvas.ConnectionManager.DefaultExcessBendLossDbPer90Deg = 0.5;
        var terminals = new[]
        {
            CrossingTestCircuit.CreateTerminal("left", 0, 95, 0),
            CrossingTestCircuit.CreateTerminal("right", 390, 95, 180),
            CrossingTestCircuit.CreateTerminal("top", 195, 40, 90),
            CrossingTestCircuit.CreateTerminal("bottom", 195, 350, 270)
        };
        foreach (var terminal in terminals) canvas.AddComponent(terminal.Component);
        foreach (var (a, b) in new[] { (2, 3), (0, 1) })
        {
            var start = terminals[a].PhysicalPin;
            var end = terminals[b].PhysicalPin;
            var (sx, sy) = start.GetAbsolutePosition();
            var (ex, ey) = end.GetAbsolutePosition();
            var path = new RoutedPath();
            path.Segments.Add(new StraightSegment(sx, sy, ex, ey, start.AngleDegrees));
            canvas.ConnectPinsWithCachedRoute(start, end, path)!.Connection.IsRouteFrozen = true;
        }
        var commands = new CommandManager();
        ComponentGroup? group = null;
        if (grouped)
        {
            commands.ExecuteCommand(new CreateGroupCommand(canvas, canvas.Components.ToList(), "Imported"));
            await canvas.RecalculateRoutesAsync();
            group = (ComponentGroup)canvas.Components.Single().Component;
        }
        if (editGroup) canvas.EnterGroupEditMode(group!);
        var binder = new CrossingInsertionCanvasBinder(canvas,
            () => new CrossingComponentInstance(CrossingTestCircuit.CreateCrossingComponent(), "Crossing", "test"),
            action => action()) { IsEnabled = true };
        binder.UndoJournal.Attach(commands);
        IUndoableCommand? owner = null;
        commands.Executing += command => owner = command;
        bool ownsRouting = true;
        canvas.Routing.BuildConnectionProcessFloorProvider = () => (_, _) =>
        {
            ownsRouting &= owner is RerouteImportedRoutesStateCommand;
            return null;
        };
        var vm = new RerouteImportedRoutesViewModel(canvas, commands);
        await vm.RerouteAllCommand.ExecuteAsync(null);
        ownsRouting.ShouldBeTrue("routing must already own its history entry");
        var afterComponents = editGroup ? canvas.Components.Select(c => c.Component).ToArray()
            : group?.ChildComponents.ToArray() ?? canvas.Components.Select(c => c.Component).ToArray();
        afterComponents.Length.ShouldBe(5, "conversion must still insert the beneficial crossing");
        commands.UndoCount.ShouldBe(grouped ? 2 : 1);
        commands.Undo().ShouldBeTrue();
        await canvas.RecalculateRoutesAsync();
        var undoneComponents = group?.ChildComponents.ToArray() ?? canvas.Components.Select(c => c.Component).ToArray();
        undoneComponents.ShouldBe(terminals.Select(t => t.Component), ignoreOrder: true);
        if (group != null)
        {
            group.InternalPaths.Count.ShouldBe(2);
            group.InternalPaths.ShouldAllBe(p => p.IsRouteFrozen);
        }
        else
        {
            canvas.Connections.Count.ShouldBe(2);
            canvas.Connections.ShouldAllBe(c => c.Connection.IsRouteFrozen);
        }
        commands.Redo().ShouldBeTrue();
        await canvas.RecalculateRoutesAsync();
        (group?.ChildComponents.ToArray() ?? canvas.Components.Select(c => c.Component).ToArray())
            .ShouldBe(afterComponents, ignoreOrder: true);
        if (group != null) group.InternalPaths.Count.ShouldBe(4);
        else canvas.Connections.Count.ShouldBe(4);
    }
}
