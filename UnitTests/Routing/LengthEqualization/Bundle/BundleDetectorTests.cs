using CAP_Core.Routing.LengthEqualization.Bundle;
using CAP_Core.Routing.MeanderGeneration;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.LengthEqualization.Bundle;

/// <summary>
/// Which selections are bundles: pins facing each other along one axis with the wires in the
/// same order at both ends. Reversed connections join the bundle; crossing orders, loops back to
/// the same column and back-to-back pins do not form one.
/// </summary>
public class BundleDetectorTests
{
    private static MeanderRequest Wire(double x0, double y0, double startDirection, double x1, double y1, double endDirection) =>
        new(x0, y0, startDirection, x1, y1, endDirection, 0, 0, 5, new MeanderBounds(-1e4, -1e4, 1e4, 1e4));

    /// <summary>Near pin at x = 0 facing +x, far pin at x = 1000 facing -x (arrival heading 0°).</summary>
    private static MeanderRequest Forward(double y0, double y1) => Wire(0, y0, 0, 1000, y1, 0);

    [Fact]
    public void Detect_FacingColumnsInOrder_SortsAcrossTheAxis()
    {
        var bundle = BundleDetector.Detect(new[] { Forward(200, 500), Forward(0, 100), Forward(100, 300) });

        bundle.ShouldNotBeNull();
        bundle.Value.HeadingDegrees.ShouldBe(0);
        bundle.Value.Wires.Select(w => w.Item).ShouldBe(new[] { 1, 2, 0 });
    }

    [Fact]
    public void Detect_ReversedConnection_JoinsFromTheFarSide()
    {
        var reversed = Wire(1000, 300, 180, 0, 100, 180);

        var bundle = BundleDetector.Detect(new[] { Forward(0, 100), reversed });

        bundle.ShouldNotBeNull();
        var wire = bundle.Value.Wires.Single(w => w.Item == 1);
        wire.Reversed.ShouldBeTrue();
        (wire.StartU, wire.StartV, wire.EndU, wire.EndV).ShouldBe((0.0, 100.0, 1000.0, 300.0));
    }

    [Fact]
    public void Detect_CrossingOrder_IsNoBundle() =>
        BundleDetector.Detect(new[] { Forward(0, 300), Forward(100, 200) }).ShouldBeNull();

    [Fact]
    public void Detect_BackToBackPins_IsNoBundle() =>
        BundleDetector.Detect(new[] { Wire(1000, 0, 0, 0, 100, 0), Wire(1000, 50, 0, 0, 150, 0) }).ShouldBeNull();

    [Fact]
    public void Detect_LoopBackToTheSameColumn_StaysOutside()
    {
        var loop = Wire(0, 400, 0, 0, 500, 180);

        var bundle = BundleDetector.Detect(new[] { Forward(0, 100), loop, Forward(100, 300) });

        bundle.ShouldNotBeNull();
        bundle.Value.Wires.Select(w => w.Item).ShouldBe(new[] { 0, 2 });
    }
}
