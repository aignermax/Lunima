using CAP_Core.Grid.GroupCollision;
using Shouldly;
using Xunit;

namespace UnitTests.Grid.GroupCollision;

/// <summary>Exact segment/rectangle distances used by the placement collision check.</summary>
public class CollisionDistanceTests
{
    [Fact]
    public void SegmentToSegment_Crossing_IsZero() =>
        CollisionDistance.SegmentToSegment(0, 0, 10, 10, 0, 10, 10, 0).ShouldBe(0);

    [Fact]
    public void SegmentToSegment_Parallel_IsOffset() =>
        CollisionDistance.SegmentToSegment(0, 0, 10, 0, 2, 3, 8, 3).ShouldBe(3, 1e-12);

    [Fact]
    public void SegmentToSegment_CollinearOverlap_IsZero() =>
        CollisionDistance.SegmentToSegment(0, 0, 10, 0, 5, 0, 15, 0).ShouldBe(0);

    [Fact]
    public void SegmentToRect_Through_IsZero() =>
        CollisionDistance.SegmentToRect(-5, 5, 15, 5, 0, 0, 10, 10).ShouldBe(0);

    [Fact]
    public void SegmentToRect_DiagonalPastCorner_IsPerpendicularDistance()
    {
        // The line x + y = 30 passes the corner (10, 10) at distance 10/√2.
        CollisionDistance.SegmentToRect(30, 0, 0, 30, 0, 0, 10, 10).ShouldBe(10 / Math.Sqrt(2), 1e-9);
    }

    [Fact]
    public void SegmentToRect_EndpointNearEdge_IsGap() =>
        CollisionDistance.SegmentToRect(12, 5, 20, 5, 0, 0, 10, 10).ShouldBe(2, 1e-12);

    [Fact]
    public void PointToSegment_BeyondEnd_IsEndpointDistance() =>
        CollisionDistance.PointToSegment(13, 4, 0, 0, 10, 0).ShouldBe(5, 1e-12);
}
