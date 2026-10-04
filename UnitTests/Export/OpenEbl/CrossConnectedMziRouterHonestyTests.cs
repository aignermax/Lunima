using CAP_Core.Routing;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// Issue #1381 — router honesty on an unavoidable crossing: the cross-connected MZI
/// arms are a non-planar two-wire pair, so after the full routing pass (ordering
/// cascade, crossing scan, contention repair) one arm still crosses the other. The
/// contention repair may accept its re-route (blocked count strictly decreases) but
/// must NOT leave the crossing wire looking clean — it keeps the blocked-fallback
/// stamp so the canvas and the design checks report the crossing.
/// </summary>
public class CrossConnectedMziRouterHonestyTests
{
    [Fact]
    public async Task CrossConnectedArms_FullRoutingPass_CrossingArmKeepsBlockedStamp()
    {
        var templates = TestPdkLoader.LoadAllTemplates()
            .Where(t => t.PdkSource == EBeamFromScratchMziDesign.EBeamPdkName).ToList();
        var canvas = await EBeamFromScratchMziDesign.BuildRoutedAsync(templates);

        var upperArm = MziFringeAnalysis.FindConnection(canvas, "mzi_splitter", "port 2");
        var lowerArm = MziFringeAnalysis.FindConnection(canvas, "mzi_splitter", "port 3");

        PathIntersectionDetector.Crosses(upperArm.RoutedPath!, lowerArm.RoutedPath!)
            .ShouldBeTrue("the cross-connected arms are non-planar — the crossing itself is expected");

        var stampedArms = new[] { upperArm, lowerArm }.Where(c => c.IsBlockedFallback).ToList();
        stampedArms.Count.ShouldBe(1,
            "exactly one side of the unavoidable crossing must keep the blocked-fallback stamp");
        stampedArms[0].FailureReason.ShouldBe(RoutingFailureReason.Contention);

        // A second full routing pass must not flip which arm crosses: an accepted
        // repair that left its crossing in place stays in the known-failed set, so the
        // stamp — and the geometry — is stable across recalculations.
        var stampedBefore = stampedArms[0];
        await canvas.RecalculateRoutesAsync();
        var stampedAfter = new[] { upperArm, lowerArm }.Where(c => c.IsBlockedFallback).ToList();
        stampedAfter.Count.ShouldBe(1, "the crossing arm must stay stamped after a re-route");
        stampedAfter[0].ShouldBeSameAs(stampedBefore,
            "the same arm must keep the stamp — the repair must not swap the crossing side");
    }
}
