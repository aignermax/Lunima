using Avalonia.Headless.XUnit;
using CAP.Avalonia.Controls.Rendering;
using CAP_Core.Routing;
using CAP_Core.Routing.MeanderGeneration;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// The cached route geometry: reused while the route and the zoom level stay the same, rebuilt
/// for another zoom level or after an in-place edit, and a zoomed-out level keeps the route's
/// extent while merging sub-pixel detail.
/// </summary>
public class RouteGeometryCacheTests
{
    private static RoutedPath Meander()
    {
        var path = new RoutedPath();
        var builder = new PosePathBuilder(path, 0, 0, 0);
        for (int loop = 0; loop < 40; loop++)
        {
            builder.AppendArc(180, 2);
            builder.AppendArc(-180, 2);
        }
        builder.AppendStraight(100);
        return path;
    }

    [AvaloniaFact]
    public void SameRouteAndZoom_ReusesTheGeometry()
    {
        var path = Meander();

        RouteGeometryCache.For(path, 1.0).ShouldBeSameAs(RouteGeometryCache.For(path, 1.2));
    }

    [AvaloniaFact]
    public void OtherZoomLevel_OrEditedRoute_IsRebuilt()
    {
        var path = Meander();
        var near = RouteGeometryCache.For(path, 1.0);

        RouteGeometryCache.For(path, 0.01).ShouldNotBeSameAs(near);

        path.Segments.Add(new StraightSegment(path.Segments[^1].EndPoint.X, 0, path.Segments[^1].EndPoint.X + 50, 0, 0));
        RouteGeometryCache.For(path, 1.0).ShouldNotBeSameAs(near);
    }

    [AvaloniaFact]
    public void ZoomedOut_KeepsTheExtent()
    {
        var path = Meander();

        var full = RouteGeometryCache.For(path, 4.0).Bounds;
        var coarse = RouteGeometryCache.For(path, 0.05).Bounds;

        coarse.Left.ShouldBe(full.Left, 10 / 0.05);
        coarse.Right.ShouldBe(full.Right, 1e-6);
        coarse.Width.ShouldBeGreaterThan(full.Width * 0.9);
    }
}
