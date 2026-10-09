using CAP_Core.Routing;
using CAP_Core.Routing.CrossingInsertion;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.CrossingInsertion;

/// <summary>
/// Cutting a routed path where a crossing sits keeps the route's own geometry: the part before
/// ends at the entry port, the part after starts at the exit port, and nothing else moves.
/// </summary>
public class RoutedPathCutterTests
{
    private const double Half = 5;

    [Fact]
    public void TryCutAround_SplitsTheStraightThroughTheCenter()
    {
        var path = new RoutedPath
        {
            Segments =
            {
                new StraightSegment(0, 0, 100, 0, 0),
                new StraightSegment(100, 0, 100, 80, 90),
            },
        };

        RoutedPathCutter.TryCutAround(path, (40, 0), Half, out var before, out var after, out var direction).ShouldBeTrue();

        direction.ShouldBe((1.0, 0.0));
        before.Segments.ShouldHaveSingleItem().EndPoint.ShouldBe((35.0, 0.0));
        after.Segments.Count.ShouldBe(2, "the part after keeps the rest of the route");
        after.Segments[0].StartPoint.ShouldBe((45.0, 0.0));
        after.Segments[1].EndPoint.ShouldBe((100.0, 80.0));
    }

    [Fact]
    public void TryCutAround_TooCloseToTheSegmentEnd_Refuses()
    {
        var path = new RoutedPath { Segments = { new StraightSegment(0, 0, 100, 0, 0) } };

        RoutedPathCutter.TryCutAround(path, (97, 0), Half, out _, out _, out _)
            .ShouldBeFalse("the crossing would stick out over the segment's end");
    }

    [Fact]
    public void TryFindRightAngleCrossing_ReturnsTheExactIntersection()
    {
        var horizontal = new RoutedPath { Segments = { new StraightSegment(0, 302, 200, 302, 0) } };
        var vertical = new RoutedPath { Segments = { new StraightSegment(98, 0, 98, 400, 90) } };

        RoutedPathCutter.TryFindRightAngleCrossing(horizontal, vertical, (100, 300), 4, out var center)
            .ShouldBeTrue("the grid quantized the crossing to (100, 300)");

        center.ShouldBe((98.0, 302.0), "the cut must sit on the real geometry, not the cell centre");
    }
}
