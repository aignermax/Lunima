using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.ViewModels.LengthMatching;

/// <summary>
/// A length-matched route survives the connection edits users make afterwards: deleting it,
/// switching its routing style (to Auto too) — each undone through the main undo history —
/// comes back with the exact matched length and its target.
/// </summary>
public class LengthMatchedConnectionUndoTests
{
    private const double Target = 400.0;

    private static (MainViewModel Vm, WaveguideConnectionViewModel Connection) MatchedDesign()
    {
        var canvas = new DesignCanvasViewModel();
        var start = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        start.WidthMicrometers = start.HeightMicrometers = 250;
        var end = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        end.WidthMicrometers = end.HeightMicrometers = 250;
        end.PhysicalX = 400;
        canvas.AddComponent(start);
        canvas.AddComponent(end);
        var startPin = start.PhysicalPins.First(p => p.Name == "out");
        var endPin = end.PhysicalPins.First(p => p.Name == "in");
        var (sx, sy) = startPin.GetAbsolutePosition();
        var (ex, ey) = endPin.GetAbsolutePosition();
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(sx, sy, ex, ey, 0));
        var connection = canvas.ConnectPinsWithCachedRoute(startPin, endPin, path)!;

        var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        var panel = vm.BottomPanel.LengthMatching;
        panel.SelectedConnection = connection;
        panel.TargetLengthText = "400";
        panel.ApplyCommand.Execute(null);
        connection.Connection.PathLengthMicrometers.ShouldBe(Target, 0.1);
        return (vm, connection);
    }

    private static void ShouldBeMatched(MainViewModel vm, WaveguideConnection connection, RoutedPath matchedPath)
    {
        vm.Canvas.ConnectionManager.Connections.ShouldContain(connection);
        vm.Canvas.Connections.ShouldContain(c => c.Connection == connection);
        connection.TargetLengthMicrometers.ShouldBe(Target);
        connection.PathLengthMicrometers.ShouldBe(Target, 0.1);
        connection.RoutedPath!.Segments.Count.ShouldBe(matchedPath.Segments.Count);
    }

    [Fact]
    public async Task DeleteThenUndo_RestoresTheMatchedRoute()
    {
        var (vm, connection) = MatchedDesign();
        var matched = connection.Connection.RoutedPath!;
        connection.IsSelected = true;
        vm.Canvas.Selection.SelectedConnections.Add(connection);

        vm.CanvasInteraction.DeleteSelectedCommand.Execute(null);
        await vm.Canvas.RecalculateRoutesAsync();
        vm.Canvas.ConnectionManager.Connections.ShouldNotContain(connection.Connection);
        vm.CommandManager.Undo().ShouldBeTrue();
        await vm.Canvas.RecalculateRoutesAsync();

        ShouldBeMatched(vm, connection.Connection, matched);
    }

    [Theory]
    [InlineData(WaveguideType.Bend)]
    [InlineData(WaveguideType.SBend)]
    [InlineData(WaveguideType.Cobra)]
    public async Task StyleChangeThenUndo_RestoresTheMatchedRoute(WaveguideType style)
    {
        var (vm, connection) = MatchedDesign();
        var matched = connection.Connection.RoutedPath!;
        var routing = vm.BottomPanel.ConnectionRouting;
        routing.SelectedConnection = connection;

        routing.SelectedStyle = style;
        await vm.Canvas.RecalculateRoutesAsync();
        vm.CommandManager.Undo().ShouldBeTrue();
        await vm.Canvas.RecalculateRoutesAsync();

        connection.Connection.Type.ShouldBe(WaveguideType.Auto);
        routing.SelectedStyle.ShouldBe(WaveguideType.Auto, "the dropdown follows the undo");
        ShouldBeMatched(vm, connection.Connection, matched);
    }

    [Fact]
    public async Task SwitchToAutoThenUndo_RestoresTheStyledMatchedRoute()
    {
        var (vm, connection) = MatchedDesign();
        var routing = vm.BottomPanel.ConnectionRouting;
        routing.SelectedConnection = connection;
        routing.SelectedStyle = WaveguideType.Cobra;
        await vm.Canvas.RecalculateRoutesAsync();
        var styled = connection.Connection.RoutedPath!;

        routing.SelectedStyle = WaveguideType.Auto;
        await vm.Canvas.RecalculateRoutesAsync();
        vm.CommandManager.Undo().ShouldBeTrue();
        await vm.Canvas.RecalculateRoutesAsync();

        connection.Connection.Type.ShouldBe(WaveguideType.Cobra);
        routing.SelectedStyle.ShouldBe(WaveguideType.Cobra, "the dropdown follows the undo");
        ShouldBeMatched(vm, connection.Connection, styled);
    }
}
