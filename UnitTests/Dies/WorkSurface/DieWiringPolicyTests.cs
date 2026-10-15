using CAP_Core.Analysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using CAP_Core.Routing.AStarPathfinder;
using Shouldly;
using Xunit;
using static UnitTests.Dies.DieTestFixtures;

namespace UnitTests.Dies.WorkSurface;

/// <summary>
/// A waveguide never crosses a die edge: across dies only facing edge-coupler facets connect
/// (as a die-to-die link); the design checks flag any legacy cross-die waveguide, and the
/// router keeps every route inside a die.
/// </summary>
public class DieWiringPolicyTests
{
    [Fact]
    public void SameDie_IsAnOrdinaryWaveguide()
    {
        var (dieA, _, partA, _, _, _) = TwoDies();
        var other = Part("other", 300, 300);
        dieA.AddChild(other);

        DieWiringPolicy.Evaluate(partA.PhysicalPins[1], other.PhysicalPins[0]).Verdict.ShouldBe(DieWiringVerdict.Waveguide);
    }

    [Fact]
    public void FacingFacetsOnTwoDies_BecomeAFacetLink()
    {
        var (_, _, _, _, facetA, facetB) = TwoDies();

        DieWiringPolicy.Evaluate(facetA, facetB).Verdict.ShouldBe(DieWiringVerdict.FacetLink);
    }

    [Fact]
    public void AnyOtherPinAcrossDies_IsRefusedWithTheCouplerHint()
    {
        var (_, _, partA, partB, facetA, _) = TwoDies();

        var decision = DieWiringPolicy.Evaluate(partA.PhysicalPins[1], partB.PhysicalPins[0]);
        decision.Verdict.ShouldBe(DieWiringVerdict.RefusedNotCouplers);
        decision.RefusalKey.ShouldBe("Dies.Wiring.CouplersOnly");
        DieWiringPolicy.Evaluate(facetA, partB.PhysicalPins[0]).Verdict.ShouldBe(DieWiringVerdict.RefusedNotCouplers);
    }

    [Fact]
    public void FacetsThatDoNotFaceEachOther_AreRefused()
    {
        var (dieA, dieB, _, _, facetA, _) = TwoDies();
        var awayCoupler = EdgeCoupler("away", 1902, 300);
        dieB.AddChild(awayCoupler);
        ComponentPoseTransform.Rotate90CounterClockwise(awayCoupler);
        ComponentPoseTransform.Rotate90CounterClockwise(awayCoupler);
        var awayFacet = EdgeCouplerFacet.FacetPinOf(awayCoupler)!;

        DieWiringPolicy.Evaluate(facetA, awayFacet).Verdict.ShouldBe(DieWiringVerdict.RefusedNotFacing);
    }

    [Fact]
    public void DesignChecks_FlagALegacyWaveguideBetweenDies()
    {
        var (dieA, dieB, partA, partB, _, _) = TwoDies();
        var legacy = new WaveguideConnection { StartPin = partA.PhysicalPins[1], EndPin = partB.PhysicalPins[0] };

        var issues = new DieAssemblyChecker().Check(new Component[] { dieA, dieB }, new[] { legacy });

        issues.ShouldContain(i => i.Type == DesignIssueType.CrossDieWaveguide && i.Connection == legacy);
    }

    [Fact]
    public void Router_KeepsRoutesInsideTheDies_TheCarrierBetweenThemIsBlocked()
    {
        var grid = new PathfindingGrid(0, 0, 2200, 500, cellSize: 4)
        {
            AllowedRegions = new[] { (0.0, 0.0, 1000.0, 500.0), (1200.0, 0.0, 1000.0, 500.0) },
        };
        grid.RebuildFromComponents(Array.Empty<Component>());

        var (insideX, insideY) = grid.PhysicalToGrid(500, 250);
        var (gapX, gapY) = grid.PhysicalToGrid(1100, 250);
        grid.IsBlocked(insideX, insideY).ShouldBeFalse();
        grid.IsBlocked(gapX, gapY).ShouldBeTrue("a waveguide may not run across the carrier between dies");
    }

    /// <summary>Die A (0..1000) and die B (1002..2002) with a part each and facing edge couplers.</summary>
    private static (ComponentGroup DieA, ComponentGroup DieB, Component PartA, Component PartB, PhysicalPin FacetA, PhysicalPin FacetB) TwoDies()
    {
        var partA = Part("pa", 200, 200);
        var couplerA = EdgeCoupler("eca", 900, 200);
        var dieA = Die("A", 0, 0, 1000, 500, null, partA, couplerA);
        var partB = Part("pb", 1500, 200);
        var couplerB = EdgeCoupler("ecb", 1002, 200);
        var dieB = Die("B", 1002, 0, 1000, 500, null, partB, couplerB);
        // Coupler A's facet points west by default; turn it to face east toward die B.
        ComponentPoseTransform.Rotate90CounterClockwise(couplerA);
        ComponentPoseTransform.Rotate90CounterClockwise(couplerA);
        return (dieA, dieB, partA, partB, EdgeCouplerFacet.FacetPinOf(couplerA)!, EdgeCouplerFacet.FacetPinOf(couplerB)!);
    }
}
