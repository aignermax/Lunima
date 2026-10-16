using CAP.Avalonia.Commands;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.CrossingInsertion;

public class MoveRouteRestorationTests
{
    [Fact]
    public void JointDrag_Undo_RestoresGeometryCapturedBeforeTranslation()
    {
        var canvas = new CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel();
        var source = CrossingTestCircuit.CreateTerminal("source", 100, 100, 0);
        var sink = CrossingTestCircuit.CreateTerminal("sink", 400, 100, 180);
        var selection = new[] { canvas.AddComponent(source.Component), canvas.AddComponent(sink.Component) };
        canvas.ConnectPins(source.PhysicalPin, sink.PhysicalPin);
        canvas.ConnectionManager.RecalculateAllTransmissions();
        var connection = canvas.ConnectionManager.Connections.ShouldHaveSingleItem();
        var before = Shape(connection.RoutedPath!);
        canvas.BeginDragComponent(selection[0]);
        foreach (var component in selection) canvas.MoveComponent(component, 0, 50);
        canvas.TranslateInternalConnectionRoutes(selection, 0, 50);
        var commands = new CommandManager();
        commands.ExecuteCommand(new GroupMoveCommand(canvas, selection, 0, 50));
        // End just the placement gesture: this regression isolates undo from drop routing.
        canvas.Placement.EndDrag();
        commands.Undo();
        Shape(connection.RoutedPath!).ShouldBe(before);
        canvas.Routing.IsRouting.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Slow")]
    public async Task ShippedCounter_MoveUndo_RestoresGeometryWithoutStartingAnotherSearch(bool throughBridge)
    {
        var scenario = await CounterCrossingScenario.LoadAsync(adaptive: false);
        var group = scenario.GroupToMove();
        var position = (group.X, group.Y);
        var connections = scenario.Manager.Connections.ToArray();
        var geometry = connections.ToDictionary(c => c, c => Shape(c.RoutedPath!));
        var crossings = scenario.Crossings();
        scenario.BlockedCount().ShouldBe(0);

        await scenario.MoveWithCommandAsync(group);
        var starts = 0;
        scenario.Canvas.Routing.GetProcessMinBendRadiusMicrometers = () => { starts++; return 0; };
        if (throughBridge)
        {
            var session = new CAP.Avalonia.Services.AgentBridge.Tools.AgentDesignSession(
                scenario.Canvas, scenario.Commands, () => null, new CAP.Avalonia.Services.ExampleDesignsService());
            var result = await new CAP.Avalonia.Services.AgentBridge.Tools.Edit.UndoTool(session)
                .ExecuteAsync(System.Text.Json.JsonDocument.Parse("{}").RootElement);
            result.IsError.ShouldBeFalse();
        }
        else scenario.Commands.Undo().ShouldBeTrue();

        starts.ShouldBe(0, "Undo must restore the saved routes rather than launch a fresh search");
        (group.X, group.Y).ShouldBe(position);
        scenario.Manager.Connections.ShouldBe(connections);
        scenario.Crossings().ShouldBe(crossings, ignoreOrder: true);
        foreach (var connection in connections)
            Shape(connection.RoutedPath!).ShouldBe(geometry[connection]);
        scenario.BlockedCount().ShouldBe(0);
        scenario.Commands.Redo().ShouldBeTrue();
        await scenario.Canvas.RecalculateRoutesAsync();
        starts.ShouldBeGreaterThan(0, "redo still routes the changed geometry");
    }

    private static object[] Shape(RoutedPath path) => path.Segments.Select(s => (object)(
        s.GetType(), s.StartPoint, s.EndPoint, s.StartAngleDegrees, s.EndAngleDegrees,
        s.LengthMicrometers)).ToArray();
}
