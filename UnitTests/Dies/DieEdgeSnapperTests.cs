using CAP_Core.Components.Dies;
using Shouldly;
using Xunit;
using static UnitTests.Dies.DieTestFixtures;

namespace UnitTests.Dies;

/// <summary>Edge couplers snap onto the nearest die edge with their facet facing out.</summary>
public class DieEdgeSnapperTests
{
    private static readonly DieOutline Outline = new(0, 0, 1000, 800);
    private const double EdgeZone = DieDefinition.DefaultEdgeZoneMicrometers;

    [Fact]
    public void NearLeftEdge_FacetAlreadyWest_SlidesOntoTheEdge()
    {
        var coupler = EdgeCoupler("ec", 30, 300);

        var snap = DieEdgeSnapper.Plan(coupler, Outline, EdgeZone)!;

        snap.Edge.ShouldBe(DieEdge.Left);
        snap.QuarterTurns.ShouldBe(0);
        snap.X.ShouldBe(0);
        snap.Y.ShouldBe(300, "it keeps its position along the edge");
    }

    [Fact]
    public void NearRightEdge_TurnsAroundSoTheFacetFacesEast()
    {
        var coupler = EdgeCoupler("ec", 870, 300);

        var snap = DieEdgeSnapper.Plan(coupler, Outline, EdgeZone)!;
        DieEdgeSnapper.Apply(coupler, snap);

        snap.Edge.ShouldBe(DieEdge.Right);
        snap.QuarterTurns.ShouldBe(2);
        var facet = EdgeCouplerFacet.FacetPinOf(coupler)!;
        facet.GetAbsoluteAngle().ShouldBe(0);
        facet.GetAbsolutePosition().x.ShouldBe(1000, 1e-9);
        DieEdgeSnapper.FacetDistanceToEdge(coupler, Outline).ShouldBe(0, 1e-9);
    }

    [Theory]
    [InlineData(400, 10, DieEdge.Top, 270)]
    [InlineData(400, 770, DieEdge.Bottom, 90)]
    public void NearTopOrBottom_FacetFacesOutward(double x, double y, DieEdge expectedEdge, double expectedAngle)
    {
        var coupler = EdgeCoupler("ec", x, y);

        var snap = DieEdgeSnapper.Plan(coupler, Outline, EdgeZone)!;
        DieEdgeSnapper.Apply(coupler, snap);

        snap.Edge.ShouldBe(expectedEdge);
        var facet = EdgeCouplerFacet.FacetPinOf(coupler)!;
        facet.GetAbsoluteAngle().ShouldBe(expectedAngle);
        DieEdgeSnapper.FacetDistanceToEdge(coupler, Outline).ShouldBe(0, 1e-9);
        coupler.PhysicalY.ShouldBeGreaterThanOrEqualTo(Outline.Y - 1e-9);
        (coupler.PhysicalY + coupler.HeightMicrometers).ShouldBeLessThanOrEqualTo(Outline.Bottom + 1e-9);
    }

    [Fact]
    public void OutsideTheEdgeZone_DoesNotSnap()
    {
        DieEdgeSnapper.Plan(EdgeCoupler("ec", 400, 300), Outline, EdgeZone).ShouldBeNull();
    }

    [Fact]
    public void FacetDistance_MeasuresAlongTheFacingDirection()
    {
        var coupler = EdgeCoupler("ec", 20, 300);

        DieEdgeSnapper.FacetDistanceToEdge(coupler, Outline).ShouldBe(20, 1e-9);
    }

    [Fact]
    public void FacetPin_IsTheFiberPin()
    {
        EdgeCouplerFacet.FacetPinOf(EdgeCoupler("ec", 0, 0))!.Name.ShouldBe("fiber");
        EdgeCouplerFacet.IsEdgeCoupler(Part("p", 0, 0)).ShouldBeFalse();
    }
}
