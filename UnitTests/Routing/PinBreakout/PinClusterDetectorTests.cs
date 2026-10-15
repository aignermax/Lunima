using CAP_Core.Components.Core;
using CAP_Core.Routing.PinBreakout;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Routing.PinBreakout;

/// <summary>
/// Tight-cluster detection: pins on one face closer than the routable pitch form a cluster
/// (across components, as a group's external pins do), anything else does not.
/// </summary>
public class PinClusterDetectorTests
{
    private const double RoutablePitch = 12.0;

    [Fact]
    public void GateInputPairEightMicrometersApart_IsOneCluster()
    {
        var a = Pin(400, 2123.5, 180);
        var b = Pin(400, 2131.5, 180);

        var clusters = PinClusterDetector.Detect(new[] { b, a }, RoutablePitch);

        clusters.Count.ShouldBe(1);
        clusters[0].HeadingDegrees.ShouldBe(180);
        clusters[0].Pins.Count.ShouldBe(2);
    }

    [Fact]
    public void ClusterPins_AreOrderedByLateralPosition()
    {
        // Heading 0 (east): lateral axis points to +y.
        var top = Pin(720, 2921.5, 0);
        var bottom = Pin(720, 2925.5, 0);

        var cluster = PinClusterDetector.Detect(new[] { bottom, top }, RoutablePitch).Single();

        cluster.Pins.ShouldBe(new[] { top, bottom });
    }

    [Fact]
    public void PinsAtRoutablePitchOrMore_AreNoCluster()
    {
        var a = Pin(0, 0, 180);
        var b = Pin(0, RoutablePitch, 180);

        PinClusterDetector.Detect(new[] { a, b }, RoutablePitch).ShouldBeEmpty();
    }

    [Fact]
    public void PinsFacingDifferentWays_AreNoCluster()
    {
        var west = Pin(0, 0, 180);
        var east = Pin(0, 4, 0);

        PinClusterDetector.Detect(new[] { west, east }, RoutablePitch).ShouldBeEmpty();
    }

    [Fact]
    public void PinsOnDifferentFaceLines_AreNoCluster()
    {
        var front = Pin(0, 0, 180);
        var setBack = Pin(20, 4, 180);

        PinClusterDetector.Detect(new[] { front, setBack }, RoutablePitch).ShouldBeEmpty();
    }

    [Fact]
    public void ChainedTightPitches_FormOneCluster_AWideGapSplits()
    {
        var pins = new[] { Pin(0, 0, 0), Pin(0, 4, 0), Pin(0, 12, 0), Pin(0, 40, 0), Pin(0, 45, 0) };

        var clusters = PinClusterDetector.Detect(pins, RoutablePitch);

        clusters.Select(c => c.Pins.Count).OrderBy(n => n).ShouldBe(new[] { 2, 3 });
    }

    [Fact]
    public void SinglePin_IsNoCluster()
    {
        PinClusterDetector.Detect(new[] { Pin(0, 0, 90) }, RoutablePitch).ShouldBeEmpty();
    }

    private static PhysicalPin Pin(double x, double y, double angle)
    {
        var component = TestComponentFactory.CreatePinlessComponent(x, y, 1, 1);
        var pin = TestComponentFactory.CreateRoutingPin(component, 0, 0, angle);
        component.PhysicalPins.Add(pin);
        return pin;
    }
}
