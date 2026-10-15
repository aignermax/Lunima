using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using Shouldly;
using Xunit;
using static UnitTests.Dies.DieTestFixtures;

namespace UnitTests.Dies;

/// <summary>Dragging a die toward another snaps facing edge couplers together at the link gap.</summary>
public class DieLinkSnapperTests
{
    [Fact]
    public void NearbyFacingFacets_SnapOnAxisAtTheLinkGap()
    {
        var (left, right) = TwoDiesWithFacingCouplers(rightDieX: 1100, rightCouplerY: 230);

        var snap = DieLinkSnapper.Plan(right, -60, 0, new[] { left, right })!;

        snap.Pairs.Count.ShouldBe(1);
        right.MoveGroup(snap.DeltaX, snap.DeltaY);
        var (ax, ay) = snap.Pairs[0].MovingFacet.GetAbsolutePosition();
        var (bx, by) = snap.Pairs[0].FixedFacet.GetAbsolutePosition();
        (ax - bx).ShouldBe(DieLinkSnapper.DefaultGapMicrometers, 1e-9);
        ay.ShouldBe(by, 1e-9, "the snap removes the lateral offset");
    }

    [Fact]
    public void FarAway_DoesNotSnap()
    {
        var (left, right) = TwoDiesWithFacingCouplers(rightDieX: 3000, rightCouplerY: 230);

        DieLinkSnapper.Plan(right, 0, 0, new[] { left, right }).ShouldBeNull();
    }

    [Fact]
    public void CouplersAtTheSamePitch_AreAllLinked()
    {
        var left = Die("L", 0, 0, 1000, 600, null, FacingEast("l1", 200), FacingEast("l2", 400));
        var right = Die("R", 1050, 0, 1000, 600, null, EdgeCoupler("r1", 1050, 200), EdgeCoupler("r2", 1050, 400));

        var snap = DieLinkSnapper.Plan(right, 0, 0, new[] { left, right })!;

        snap.Pairs.Count.ShouldBe(2);
    }

    [Fact]
    public void CouplersAwayFromTheEdge_AreIgnored()
    {
        var left = Die("L", 0, 0, 1000, 600, null, FacingEast("l1", 200));
        var right = Die("R", 1010, 0, 1000, 600, null, EdgeCoupler("r1", 1300, 200));

        DieLinkSnapper.EdgeFacets(right).ShouldBeEmpty();
        DieLinkSnapper.Plan(right, 0, 0, new[] { left, right }).ShouldBeNull();
    }

    [Fact]
    public void SpotSizeMismatch_CostsLight_EvenAtZeroGap()
    {
        double matched = ChipletEdgeCouplerCoupling.PowerCoupling(1.5, 1.5, 0, 0, 1550);
        double mismatched = ChipletEdgeCouplerCoupling.PowerCoupling(1.5, 3.0, 0, 0, 1550);

        matched.ShouldBe(1.0, 1e-12);
        mismatched.ShouldBe(Math.Pow(2 * 1.5 * 3.0 / (1.5 * 1.5 + 3.0 * 3.0), 2), 1e-12);
    }

    [Fact]
    public void IdenticalModes_ReproduceTheGapAndOffsetModel()
    {
        double general = ChipletEdgeCouplerCoupling.PowerCoupling(
            ChipletEdgeCouplerCoupling.ModeWaistMicrometers, ChipletEdgeCouplerCoupling.ModeWaistMicrometers, 0.7, 3.0, 1550);
        double legacy = ChipletEdgeCouplerCoupling.PowerCouplingForOffset(0.7)
                        * ChipletEdgeCouplerCoupling.PowerCouplingForGap(3.0, 1550);

        general.ShouldBe(legacy, 1e-12);
    }

    [Fact]
    public void IndexMatchedGap_LosesLessThanAir()
    {
        double air = ChipletEdgeCouplerCoupling.PowerCoupling(1.5, 1.5, 0, 10, 1550, 1.0);
        double adhesive = ChipletEdgeCouplerCoupling.PowerCoupling(1.5, 1.5, 0, 10, 1550, 1.45);

        adhesive.ShouldBeGreaterThan(air);
    }

    [Fact]
    public void WaistOf_UsesThePdkModeFieldDiameter()
    {
        var coupler = EdgeCoupler("ec", 0, 0);
        ChipletEdgeCouplerCoupling.WaistOf(coupler).ShouldBe(ChipletEdgeCouplerCoupling.ModeWaistMicrometers);

        coupler.ModeFieldDiameterMicrometers = 9.0;

        ChipletEdgeCouplerCoupling.WaistOf(coupler).ShouldBe(4.5);
    }

    /// <summary>An edge coupler on the left die's east edge (x = 1000), facet facing east.</summary>
    private static Component FacingEast(string id, double y)
    {
        var coupler = EdgeCoupler(id, 1000 - CouplerWidth, y);
        DieEdgeSnapper.Apply(coupler, DieEdgeSnapper.Plan(coupler, new DieOutline(0, 0, 1000, 600), 50)!);
        return coupler;
    }

    private static (ComponentGroup Left, ComponentGroup Right) TwoDiesWithFacingCouplers(double rightDieX, double rightCouplerY)
    {
        var left = Die("L", 0, 0, 1000, 600, null, FacingEast("l1", 200));
        var right = Die("R", rightDieX, 0, 1000, 600, null, EdgeCoupler("r1", rightDieX, rightCouplerY));
        return (left, right);
    }
}
