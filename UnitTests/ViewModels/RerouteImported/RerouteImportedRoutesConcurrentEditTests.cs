using UnitTests.ViewModels.Canvas;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.RerouteImported;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels.RerouteImported;

public partial class RerouteImportedRoutesViewModelTests
{
    [Fact]
    public async Task SelectedGroupConversion_RedoDoesNotReplaySeparatelyUndoneOtherRouteSetting()
    {
        var canvas = ReportedRoutingWorker.CreateCanvas();
        var (start, end) = AddComponentPair(canvas, 0);
        ConnectFrozenImported(canvas, start, end);
        var (otherStart, otherEnd) = AddComponentPair(canvas, 2000);
        ConnectFrozenImported(canvas, otherStart, otherEnd).Connection.BendRadiusMicrometers = 25;
        var commands = new CommandManager();
        commands.ExecuteCommand(new CreateGroupCommand(canvas, canvas.Components.ToList(), "Imported"));
        await canvas.RecalculateRoutesAsync();
        var group = (ComponentGroup)canvas.Components.Single().Component;
        canvas.EnterGroupEditMode(group);
        var vm = new RerouteImportedRoutesViewModel(canvas, commands)
        {
            SelectedConnection = canvas.Connections.Single(c => c.Connection.StartPin == start)
        };
        var routing = (ReportedRoutingWorker)canvas.Routing;
        routing.BlockWorker = true;
        var edit = new ConcurrentRouteSetting(canvas, otherStart, 25, 75);
        var conversion = vm.RerouteSelectedCommand.ExecuteAsync(null);
        try
        {
            await routing.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            commands.ExecuteCommand(edit);
        }
        finally { routing.Release.TrySetResult(); await conversion.WaitAsync(TimeSpan.FromSeconds(30)); }

        commands.UndoDescription.ShouldBe(edit.Description);
        commands.Undo().ShouldBeTrue();
        await canvas.RecalculateRoutesAsync();
        commands.Undo().ShouldBeTrue();
        await canvas.RecalculateRoutesAsync();
        commands.Redo().ShouldBeTrue();
        await canvas.RecalculateRoutesAsync();
        canvas.Connections.Single(c => c.Connection.StartPin == otherStart)
            .Connection.BendRadiusMicrometers.ShouldBe(25,
                "redoing selected-route conversion must not replay an unrelated route's undone edit");
        group.InternalPaths.Single(p => p.StartPin == otherStart).BendRadiusMicrometers.ShouldBe(25);
        commands.Redo().ShouldBeTrue();
        canvas.Connections.Single(c => c.Connection.StartPin == otherStart)
            .Connection.BendRadiusMicrometers.ShouldBe(75);
    }

    [Fact]
    public async Task OpenGroupConversion_RedoDoesNotResurrectSeparatelyUndonePlacement()
    {
        var canvas = ReportedRoutingWorker.CreateCanvas();
        var (start, end) = AddComponentPair(canvas, 0);
        ConnectFrozenImported(canvas, start, end);
        var commands = new CommandManager();
        commands.ExecuteCommand(new CreateGroupCommand(canvas, canvas.Components.ToList(), "Imported"));
        await canvas.RecalculateRoutesAsync();
        var group = (ComponentGroup)canvas.Components.Single().Component;
        canvas.EnterGroupEditMode(group);
        var vm = new RerouteImportedRoutesViewModel(canvas, commands)
        {
            SelectedConnection = canvas.Connections.Single()
        };
        var routing = (ReportedRoutingWorker)canvas.Routing;
        routing.BlockWorker = true;
        var added = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        added.PhysicalX = 3000;
        added.PhysicalY = 3000;
        var placement = new ConcurrentGroupPlacement(canvas, added);
        var conversion = vm.RerouteSelectedCommand.ExecuteAsync(null);
        try
        {
            await routing.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            commands.ExecuteCommand(placement);
        }
        finally { routing.Release.TrySetResult(); await conversion.WaitAsync(TimeSpan.FromSeconds(30)); }

        canvas.CurrentEditGroup.ShouldBeSameAs(group);
        commands.UndoDescription.ShouldBe(placement.Description,
            "a later placement must remain above the asynchronous conversion in history");
        commands.Undo().ShouldBeTrue();
        await canvas.RecalculateRoutesAsync();
        canvas.Components.ShouldNotContain(c => ReferenceEquals(c.Component, added));
        commands.Undo().ShouldBeTrue();
        await canvas.RecalculateRoutesAsync();
        commands.Redo().ShouldBeTrue();
        await canvas.RecalculateRoutesAsync();

        canvas.Components.ShouldNotContain(c => ReferenceEquals(c.Component, added),
            "conversion redo must not absorb a placement made while its worker awaited");
        group.ChildComponents.ShouldNotContain(added);
        commands.Redo().ShouldBeTrue();
        canvas.Components.ShouldContain(c => ReferenceEquals(c.Component, added),
            "the independent placement remains redoable as its own history entry");
    }

    private sealed class ConcurrentGroupPlacement(DesignCanvasViewModel canvas, Component component) : IUndoableCommand
    {
        public string Description => "Place unrelated component during conversion";
        public void Execute() => canvas.AddComponent(component);
        public void Undo()
        {
            var viewModel = canvas.Components.Single(c => ReferenceEquals(c.Component, component));
            canvas.RemoveComponent(viewModel);
        }
    }

    private sealed class ConcurrentRouteSetting(
        DesignCanvasViewModel canvas, PhysicalPin start, double before, double after) : IUndoableCommand
    {
        public string Description => "Edit unrelated route during conversion";
        public void Execute() => SetRadius(after);
        public void Undo() => SetRadius(before);
        private void SetRadius(double radius) => canvas.Connections.Single(c => c.Connection.StartPin == start)
            .Connection.BendRadiusMicrometers = radius;
    }
}
