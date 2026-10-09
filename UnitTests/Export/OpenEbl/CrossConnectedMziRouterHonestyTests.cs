using CAP_Core.Routing;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// Router honesty on the cross-connected MZI arms: the pair is only planar if one arm loops
/// around the splitter, so the second-routed arm must avoid the first — never cross it while
/// looking clean. The routed result is planar with both arms connected, the detour is the
/// arm-length difference the fringe measurement relies on, and a second full routing pass
/// keeps it.
/// </summary>
public class CrossConnectedMziRouterHonestyTests
{
    [Fact]
    public async Task CrossConnectedArms_FullRoutingPass_AvoidEachOtherInsteadOfCrossing()
    {
        var templates = TestPdkLoader.LoadAllTemplates()
            .Where(t => t.PdkSource == EBeamFromScratchMziDesign.EBeamPdkName).ToList();
        var canvas = await EBeamFromScratchMziDesign.BuildRoutedAsync(templates);

        var upperArm = MziFringeAnalysis.FindConnection(canvas, "mzi_splitter", "port 2");
        var lowerArm = MziFringeAnalysis.FindConnection(canvas, "mzi_splitter", "port 3");

        upperArm.IsBlockedFallback.ShouldBeFalse();
        lowerArm.IsBlockedFallback.ShouldBeFalse();
        PathIntersectionDetector.Crosses(upperArm.RoutedPath!, lowerArm.RoutedPath!)
            .ShouldBeFalse("a clean-looking arm must never cut through the other one");
        Math.Abs(upperArm.PathLengthMicrometers - lowerArm.PathLengthMicrometers)
            .ShouldBeGreaterThan(0, "the avoiding arm is the longer one — the ΔL of the interferometer");

        var upperBefore = upperArm.PathLengthMicrometers;
        var lowerBefore = lowerArm.PathLengthMicrometers;
        await canvas.RecalculateRoutesAsync();
        upperArm.PathLengthMicrometers.ShouldBe(upperBefore, 1e-6, "a second pass must keep the planar routes");
        lowerArm.PathLengthMicrometers.ShouldBe(lowerBefore, 1e-6);
        PathIntersectionDetector.Crosses(upperArm.RoutedPath!, lowerArm.RoutedPath!).ShouldBeFalse();
    }
}
