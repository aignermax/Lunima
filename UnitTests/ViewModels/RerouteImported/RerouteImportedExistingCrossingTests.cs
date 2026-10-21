using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP.Avalonia.ViewModels.Canvas.RerouteImported;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_Core.Routing.CrossingInsertion;
using Shouldly;
using UnitTests.Routing.CrossingInsertion;
using Xunit;

namespace UnitTests.ViewModels.RerouteImported;

public partial class RerouteImportedRoutesViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConversionTopology_RemovedCrossingUndoRestoresItsFourWires(bool grouped)
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
        var binder = new CrossingInsertionCanvasBinder(canvas,
            () => new CrossingComponentInstance(CrossingTestCircuit.CreateCrossingComponent(), "Crossing", "test"),
            action => action()) { IsEnabled = true };
        binder.UndoJournal.Attach(commands);
        var vm = new RerouteImportedRoutesViewModel(canvas, commands);
        await vm.RerouteAllCommand.ExecuteAsync(null);
        var crossing = canvas.Components.Single(c => c.Component.IsInsertedCrossing).Component;
        var originalWires = canvas.Connections.Select(c => c.Connection).ToArray();
        originalWires.Length.ShouldBe(4);

        binder.IsEnabled = false;
        foreach (var wire in originalWires) wire.IsRouteFrozen = true;
        ComponentGroup? group = null;
        if (grouped)
        {
            commands.ExecuteCommand(new CreateGroupCommand(canvas, canvas.Components.ToList(), "Existing crossing"));
            await canvas.RecalculateRoutesAsync();
            group = (ComponentGroup)canvas.Components.Single().Component;
        }
        Component[] Components() => group?.ChildComponents.ToArray()
            ?? canvas.Components.Select(c => c.Component).ToArray();
        int WireCount() => group?.InternalPaths.Count ?? canvas.Connections.Count;
        var groupStates = new List<RerouteImportedRoutesStateCommand.GroupState>();
        if (group != null)
        {
            groupStates.Add(new RerouteImportedRoutesStateCommand.GroupState
            {
                Group = group,
                OldChildren = group.ChildComponents.ToArray(),
                OldPaths = group.InternalPaths.Select(RerouteImportedRoutesStateCommand.CloneWithPins).ToArray()
            });
            canvas.EnterGroupEditMode(group);
        }
        var state = new RerouteImportedRoutesStateCommand(canvas, [], groupStates)
        {
            RoutingGroup = group,
            RootTopology = group == null ? new RerouteImportedRootTopology(canvas) : null
        };
        state.RootTopology?.Prepare();
        binder.Service.ChangeRecorded = state.RecordCrossingChange;
        canvas.ConnectionManager.RemoveAllInsertedCrossings();
        state.RecordCrossingChange(new CrossingChange([], [crossing],
            canvas.ConnectionManager.Connections.ToArray(), () => { }));
        await canvas.RecalculateRoutesAsync();
        if (group != null)
        {
            canvas.ExitGroupEditMode();
            await canvas.RecalculateRoutesAsync();
            var finalPaths = group.InternalPaths.Select(RerouteImportedRoutesStateCommand.CloneWithPins).ToArray();
            groupStates[0].NewChildren = group.ChildComponents.ToArray();
            groupStates[0].CreateNewPaths = () => finalPaths.Select(RerouteImportedRoutesStateCommand.CloneWithPins).ToArray();
            groupStates[0].NewPathCount = finalPaths.Length;
            state.SealGroupTopology();
        }
        else state.RootTopology!.CaptureAfter();
        state.Execute();
        Components().ShouldNotContain(crossing,
            "the fixture must record an actual removal of the pre-existing crossing");
        WireCount().ShouldBe(2);

        state.Undo();
        await canvas.RecalculateRoutesAsync();
        Components().ShouldContain(crossing);
        WireCount().ShouldBe(4,
            "restoring a removed crossing must restore all four wires incident to its pins");
        if (group != null)
        {
            group.InternalPaths.Select(p => (p.StartPin, p.EndPin))
                .ShouldBe(originalWires.Select(c => (c.StartPin, c.EndPin)), ignoreOrder: true);
            group.InternalPaths.ShouldAllBe(p => p.IsRouteFrozen);
        }
        else
        {
            canvas.Connections.Select(c => c.Connection).ShouldBe(originalWires, ignoreOrder: true);
            canvas.ConnectionManager.Connections.ShouldBe(originalWires, ignoreOrder: true);
            canvas.Connections.ShouldAllBe(c => c.Connection.IsRouteFrozen);
        }

        state.Execute();
        await canvas.RecalculateRoutesAsync();
        Components().ShouldNotContain(crossing);
        WireCount().ShouldBe(2);
    }
}
