using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

/// <summary>
/// Tests for the per-pass wall-clock breakdown of a full re-route
/// (<see cref="WaveguideConnectionManager.LastRoutingPassTimings"/>): every pass reports a
/// non-negative duration, the measured passes never add up to more than the total, and a
/// cancelled re-route still reports the passes that ran.
/// </summary>
public class RoutingPassTimingsTests
{
    private const double BendRadius = 10.0;

    [Fact]
    public void CompletedReroute_EveryPassReportsNonNegativeDuration_SumWithinTotal()
    {
        var manager = CreateManager(out var source, out var target);

        manager.AddConnection(CreatePin(source, 50, 25, 0), CreatePin(target, 0, 25, 180));

        var timings = manager.LastRoutingPassTimings;
        timings.ShouldNotBeNull("a completed re-route must publish its per-pass breakdown");
        timings.WasCancelled.ShouldBeFalse();
        timings.Total.ShouldBeGreaterThan(TimeSpan.Zero);
        timings.CrossingDissolution.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        timings.InitialPass.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        timings.OrderingCascade.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        timings.CrossingInsertion.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        timings.PinLeadCollapse.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        timings.BendUpsizing.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        timings.CrossingScan.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        timings.ContentionRepair.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        timings.MeasuredPassSum.ShouldBeLessThanOrEqualTo(timings.Total);
        timings.OrderingAttempts.ShouldBe(manager.LastOrderingAttemptCount);
        timings.ContentionRepairAttempts.ShouldBe(manager.LastContentionRepairAttemptCount);
        timings.ContentionRepairAccepts.ShouldBe(manager.LastContentionRepairAcceptCount);
    }

    [Fact]
    public void CancelledReroute_ReportsWhatRan_SumWithinTotal()
    {
        var manager = CreateManager(out var source, out var target);
        manager.AddConnectionDeferred(CreatePin(source, 50, 25, 0), CreatePin(target, 0, 25, 180));
        manager.AddConnectionDeferred(CreatePin(source, 50, 40, 0), CreatePin(target, 0, 40, 180));

        using var cancellation = new CancellationTokenSource();
        manager.RecalculateAllTransmissions(
            progressCallback: () => cancellation.Cancel(),
            cancellationToken: cancellation.Token);

        var timings = manager.LastRoutingPassTimings;
        timings.ShouldNotBeNull("even a cancelled re-route must publish the passes that ran");
        timings.WasCancelled.ShouldBeTrue();
        timings.InitialPass.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        timings.OrderingCascade.ShouldBe(TimeSpan.Zero,
            "cancellation during the initial pass must stop before the ordering cascade");
        timings.ContentionRepairAttempts.ShouldBe(0,
            "the contention-repair pass never ran — a stale count from an earlier pass must not leak in");
        timings.ContentionRepairAccepts.ShouldBe(0);
        timings.MeasuredPassSum.ShouldBeLessThanOrEqualTo(timings.Total);
    }

    [Fact]
    public void MeasuredPassSum_AddsUpAllPasses()
    {
        var timings = new RoutingPassTimings
        {
            Total = TimeSpan.FromSeconds(10),
            CrossingDissolution = TimeSpan.FromSeconds(1),
            InitialPass = TimeSpan.FromSeconds(1),
            OrderingCascade = TimeSpan.FromSeconds(1),
            CrossingInsertion = TimeSpan.FromSeconds(1),
            PinLeadCollapse = TimeSpan.FromSeconds(1),
            BendUpsizing = TimeSpan.FromSeconds(1),
            CrossingScan = TimeSpan.FromSeconds(1),
            ContentionRepair = TimeSpan.FromSeconds(1),
        };

        timings.MeasuredPassSum.ShouldBe(TimeSpan.FromSeconds(8));
        timings.MeasuredPassSum.ShouldBeLessThanOrEqualTo(timings.Total);
    }

    private static WaveguideConnectionManager CreateManager(out Component source, out Component target)
    {
        source = CreateTestComponent(0, 200);
        target = CreateTestComponent(300, 200);
        var router = new WaveguideRouter
        {
            MinBendRadiusMicrometers = BendRadius,
            MinWaveguideSpacingMicrometers = 2.0
        };
        router.InitializePathfindingGrid(-100, -100, 500, 500, new[] { source, target });
        return new WaveguideConnectionManager(router) { UseSequentialRouting = true };
    }

    private static PhysicalPin CreatePin(Component comp, double offsetX, double offsetY, double angle)
    {
        return new PhysicalPin
        {
            Name = angle < 90 || angle > 270 ? "output" : "input",
            OffsetXMicrometers = offsetX,
            OffsetYMicrometers = offsetY,
            AngleDegrees = angle,
            ParentComponent = comp
        };
    }

    private static Component CreateTestComponent(double x, double y, double width = 50, double height = 50)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "test",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: $"Test_{x}_{y}",
            rotationCounterClock: DiscreteRotation.R0
        );

        component.WidthMicrometers = width;
        component.HeightMicrometers = height;
        component.PhysicalX = x;
        component.PhysicalY = y;

        return component;
    }
}
