using CAP_Core.Routing;
using CAP_Core.Routing.ImportedGeometry;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.ImportedGeometry;

public class FrozenRouteChainerTests
{
    private const double Width = 2.0;

    /// <summary>
    /// An L-route: straight east, 90° left turn (R=50), straight north — drawn as
    /// three separate ribbons, shuffled and partly reversed like a GDS file has them.
    /// </summary>
    private static List<RibbonFit> ShuffledLRoute()
    {
        var first = Fit(RibbonTestPolygons.Straight(0, 0, 100, 0, Width));
        var bend = Fit(RibbonTestPolygons.Arc(new BendSegment(100, 50, 50, 0, 90), Width));
        var last = Fit(RibbonTestPolygons.Straight(150, 50, 150, 200, Width));
        return new List<RibbonFit> { last.Reversed(), first, bend.Reversed() };
    }

    [Fact]
    public void Chain_ShuffledPieces_ProducesContinuousPathPinToPin()
    {
        var path = FrozenRouteChainer.Chain(ShuffledLRoute(), (0, 0), (150, 200)).ShouldNotBeNull();

        path.IsValid.ShouldBeTrue();
        path.Segments[0].StartPoint.ShouldBe((0, 0));
        path.Segments[^1].EndPoint.X.ShouldBe(150, 1e-6);
        path.Segments[^1].EndPoint.Y.ShouldBe(200, 1e-6);
        path.TotalLengthMicrometers.ShouldBe(100 + Math.PI * 25 + 150, 1e-3);
        path.TotalEquivalent90DegreeBends.ShouldBe(1.0, 1e-6);
    }

    [Fact]
    public void Chain_FromTheOtherPin_TraversesBackwards()
    {
        var path = FrozenRouteChainer.Chain(ShuffledLRoute(), (150, 200), (0, 0)).ShouldNotBeNull();

        path.IsValid.ShouldBeTrue();
        path.Segments[0].StartPoint.X.ShouldBe(150, 1e-6);
        path.Segments[^1].EndPoint.X.ShouldBe(0, 1e-6);
        path.TotalLengthMicrometers.ShouldBe(100 + Math.PI * 25 + 150, 1e-3);
    }

    [Fact]
    public void Chain_SmallJointGap_IsBridgedWithStraight()
    {
        var a = Fit(RibbonTestPolygons.Straight(0, 0, 100, 0, Width));
        var b = Fit(RibbonTestPolygons.Straight(100.04, 0, 200, 0, Width));

        var path = FrozenRouteChainer.Chain(new[] { a, b }, (0, 0), (200, 0)).ShouldNotBeNull();

        path.IsValid.ShouldBeTrue();
        path.Segments.Count.ShouldBe(3);
        path.TotalLengthMicrometers.ShouldBe(200, 1e-6);
    }

    [Fact]
    public void Chain_GapLargerThanLimit_ReturnsNull()
    {
        var a = Fit(RibbonTestPolygons.Straight(0, 0, 100, 0, Width));
        var b = Fit(RibbonTestPolygons.Straight(110, 0, 200, 0, Width));

        FrozenRouteChainer.Chain(new[] { a, b }, (0, 0), (200, 0)).ShouldBeNull();
    }

    [Fact]
    public void Chain_EndPinFarFromLastPiece_ReturnsNull()
    {
        var a = Fit(RibbonTestPolygons.Straight(0, 0, 100, 0, Width));

        FrozenRouteChainer.Chain(new[] { a }, (0, 0), (100, 30)).ShouldBeNull();
    }

    [Fact]
    public void Chain_NoPieces_ReturnsNull()
    {
        FrozenRouteChainer.Chain(Array.Empty<RibbonFit>(), (0, 0), (1, 0)).ShouldBeNull();
    }

    private static RibbonFit Fit(List<(double X, double Y)> outline) =>
        RibbonCenterlineFitter.Fit(outline) ?? throw new InvalidOperationException("fixture is not a ribbon");
}
