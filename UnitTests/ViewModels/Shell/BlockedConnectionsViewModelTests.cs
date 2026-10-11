using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Shell;
using CAP_Core.Routing;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.ViewModels.Shell;

/// <summary>
/// The status-bar warning field counts blocked fallback routes and walks through them:
/// each command run selects the next blocked connection and centers the canvas on it.
/// </summary>
public class BlockedConnectionsViewModelTests
{
    private const double ComponentSpacing = 400;

    [Fact]
    public void Count_IsZero_AndChipHidden_WhenNothingIsBlocked()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        AddConnections(vm.Canvas, blocked: false, count: 2);

        vm.BlockedConnections.BlockedCount.ShouldBe(0);
        vm.BlockedConnections.HasBlocked.ShouldBeFalse();
    }

    [Fact]
    public void Count_TracksBlockedConnections_AsTheyAreAddedAndRemoved()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        AddConnections(vm.Canvas, blocked: false, count: 1);
        var blocked = AddConnections(vm.Canvas, blocked: true, count: 2);

        vm.BlockedConnections.BlockedCount.ShouldBe(2);
        vm.BlockedConnections.HasBlocked.ShouldBeTrue();

        vm.Canvas.Connections.Remove(blocked[0]);
        vm.BlockedConnections.BlockedCount.ShouldBe(1);
    }

    [Fact]
    public void Count_FollowsAPathThatBecomesBlocked()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var connection = AddConnections(vm.Canvas, blocked: false, count: 1)[0];

        connection.Connection.RoutedPath!.IsBlockedFallback = true;
        connection.NotifyPathChanged();

        vm.BlockedConnections.BlockedCount.ShouldBe(1);
    }

    [Fact]
    public void SelectNextBlocked_SelectsEachBlockedConnectionInTurn_AndWraps()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        AddConnections(vm.Canvas, blocked: false, count: 1);
        var blocked = AddConnections(vm.Canvas, blocked: true, count: 2);

        vm.BlockedConnections.SelectNextBlockedCommand.Execute(null);
        vm.CanvasInteraction.SelectedWaveguideConnection.ShouldBe(blocked[0]);
        blocked[0].IsSelected.ShouldBeTrue();

        vm.BlockedConnections.SelectNextBlockedCommand.Execute(null);
        vm.CanvasInteraction.SelectedWaveguideConnection.ShouldBe(blocked[1]);
        blocked[0].IsSelected.ShouldBeFalse("the previous connection is deselected");

        vm.BlockedConnections.SelectNextBlockedCommand.Execute(null);
        vm.CanvasInteraction.SelectedWaveguideConnection.ShouldBe(blocked[0], "the walk wraps around");
    }

    [Fact]
    public void SelectNextBlocked_ClearsComponentSelection_AndCentersOnTheConnection()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var blocked = AddConnections(vm.Canvas, blocked: true, count: 1)[0];
        var component = vm.Canvas.Components[0];
        vm.Canvas.Selection.SelectSingle(component);
        vm.CanvasInteraction.SelectedComponent = component;
        double panXBefore = vm.Canvas.PanX;

        vm.BlockedConnections.SelectNextBlockedCommand.Execute(null);

        vm.Canvas.Selection.SelectedComponents.ShouldBeEmpty();
        vm.CanvasInteraction.SelectedComponent.ShouldBeNull();
        double midX = (blocked.StartX + blocked.EndX) / 2;
        vm.Canvas.PanX.ShouldNotBe(panXBefore);
        var (width, _) = vm.ViewportControl.GetViewportSize?.Invoke() ?? (900, 800);
        vm.Canvas.PanX.ShouldBe(width / 2 - midX * vm.ViewportControl.ZoomLevel, 1e-6);
    }

    [Fact]
    public void SelectNextBlocked_DoesNothing_WhenNothingIsBlocked()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        AddConnections(vm.Canvas, blocked: false, count: 1);

        vm.BlockedConnections.SelectNextBlockedCommand.Execute(null);

        vm.CanvasInteraction.SelectedWaveguideConnection.ShouldBeNull();
    }

    /// <summary>Places waveguide pairs side by side and connects them with a cached straight route.</summary>
    private static List<WaveguideConnectionViewModel> AddConnections(
        DesignCanvasViewModel canvas, bool blocked, int count)
    {
        var added = new List<WaveguideConnectionViewModel>();
        for (int i = 0; i < count; i++)
        {
            double x = canvas.Components.Count * ComponentSpacing;
            var left = Place(canvas, x);
            var right = Place(canvas, x + ComponentSpacing / 2);
            var start = left.PhysicalPins.First(p => p.Name == "out");
            var end = right.PhysicalPins.First(p => p.Name == "in");
            var (sx, sy) = start.GetAbsolutePosition();
            var (ex, ey) = end.GetAbsolutePosition();
            var path = new RoutedPath { IsBlockedFallback = blocked };
            path.Segments.Add(new StraightSegment(sx, sy, ex, ey, 0));
            added.Add(canvas.ConnectPinsWithCachedRoute(start, end, path)!);
        }
        return added;
    }

    private static CAP_Core.Components.Core.Component Place(DesignCanvasViewModel canvas, double x)
    {
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        component.PhysicalX = x;
        component.PhysicalY = 0;
        canvas.AddComponent(component);
        return component;
    }
}
