using CAP_Core.LightCalculation.BendLoss;
using CAP_Core.Routing;
using CAP_Core.Routing.InterconnectRouting;
using Shouldly;
using Xunit;

namespace UnitTests.LightCalculation.BendLoss;

/// <summary>
/// Itemized path loss: propagation in dB/cm over the length, radiation per bend, junction loss
/// per curvature change (including the straight component ports at both ends), sampled smooth
/// curves integrated vertex by vertex. Never a gain, never NaN.
/// </summary>
public class ConnectionLossCalculatorTests
{
    private static readonly BendLossModel Model = BendLossPresets.Generic;
    private static double J(double radius) => Model.JunctionLossDb(1.0 / radius);

    [Fact]
    public void StraightPath_HasOnlyPropagationLoss_InDbPerCm()
    {
        var path = Path(new StraightSegment(0, 0, 10_000, 0, 0));

        var loss = ConnectionLossCalculator.Compute(path, Model, propagationLossDbPerCm: 2.0);

        loss.PropagationLossDb.ShouldBe(2.0, 1e-9, "1 cm at 2 dB/cm");
        loss.BendLossDb.ShouldBe(0);
        loss.JunctionLossDb.ShouldBe(0);
        loss.JunctionCount.ShouldBe(0);
        loss.BendCount.ShouldBe(0);
        loss.TotalLossDb.ShouldBe(2.0, 1e-9);
    }

    [Fact]
    public void Corner_HasRadiationAndTwoJunctions()
    {
        var path = Path(
            new StraightSegment(0, 0, 50, 0, 0),
            new BendSegment(50, 10, 10, 0, 90),
            new StraightSegment(60, 10, 60, 60, 90));

        var loss = ConnectionLossCalculator.Compute(path, Model, 0);

        loss.BendCount.ShouldBe(1);
        loss.Bends[0].RadiusMicrometers.ShouldBe(10);
        loss.BendLossDb.ShouldBe(Model.RadiationLossDb(10, 90), 1e-12);
        loss.JunctionCount.ShouldBe(2);
        loss.JunctionLossDb.ShouldBe(2 * J(10), 1e-12);
        loss.TotalLossDb.ShouldBe(Model.CornerLossDb(10, 90), 1e-12);
    }

    [Fact]
    public void ArcAtThePathEnds_CountsJunctionsToTheStraightPorts()
    {
        var path = Path(new BendSegment(0, 10, 10, 0, 90));

        var loss = ConnectionLossCalculator.Compute(path, Model, 0);

        loss.JunctionCount.ShouldBe(2);
        loss.JunctionLossDb.ShouldBe(2 * J(10), 1e-12);
    }

    [Fact]
    public void SBendReversal_CostsFourTimesAStraightJunction()
    {
        var path = Path(
            new StraightSegment(0, 0, 10, 0, 0),
            new BendSegment(10, 20, 20, 0, 45),
            new BendSegment(30, -10, 20, 45, -45),
            new StraightSegment(40, 10, 60, 10, 0));

        var loss = ConnectionLossCalculator.Compute(path, Model, 0);

        loss.JunctionCount.ShouldBe(3);
        loss.JunctionLossDb.ShouldBe(J(20) + 4 * J(20) + J(20), 1e-12);
    }

    [Fact]
    public void ConsecutiveArcsOfEqualCurvature_HaveNoJunctionBetweenThem()
    {
        var path = Path(
            new StraightSegment(0, 0, 10, 0, 0),
            new BendSegment(10, 20, 20, 0, 45),
            new BendSegment(10, 20, 20, 45, 45),
            new StraightSegment(30, 20, 30, 60, 90));

        var loss = ConnectionLossCalculator.Compute(path, Model, 0);

        loss.JunctionCount.ShouldBe(2);
        loss.BendLossDb.ShouldBe(Model.RadiationLossDb(20, 90), 1e-12);
    }

    [Fact]
    public void TightCorner_LosesMoreThanGentleCorner()
    {
        var tight = ConnectionLossCalculator.Compute(Corner(5), Model, 0.5);
        var gentle = ConnectionLossCalculator.Compute(Corner(50), Model, 0.5);

        tight.TotalLossDb.ShouldBeGreaterThan(gentle.TotalLossDb * 10);
        tight.AmplitudeTransmission.ShouldBeLessThan(gentle.AmplitudeTransmission);
    }

    [Fact]
    public void ExcessLossPer90_AddsPerQuarterTurn()
    {
        var plain = ConnectionLossCalculator.Compute(Corner(10), Model, 0);
        var withExcess = ConnectionLossCalculator.Compute(Corner(10), Model, 0, excessBendLossDbPer90Deg: 0.2);

        (withExcess.BendLossDb - plain.BendLossDb).ShouldBe(0.2, 1e-12);
    }

    [Fact]
    public void DegenerateZeroRadiusArc_StaysPassiveAndFinite()
    {
        var path = Path(new StraightSegment(0, 0, 10, 0, 0), new BendSegment(10, 0, 0, 0, 90));

        var loss = ConnectionLossCalculator.Compute(path, Model, 0.5);

        double.IsNaN(loss.TotalLossDb).ShouldBeFalse();
        loss.TotalLossDb.ShouldBeLessThanOrEqualTo(BendLossModel.MaxLossDb);
        loss.AmplitudeTransmission.ShouldBeGreaterThan(0);
        loss.AmplitudeTransmission.ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public void NullOrEmptyPath_IsLossless()
    {
        ConnectionLossCalculator.Compute(null, Model, 1).AmplitudeTransmission.ShouldBe(1);
        ConnectionLossCalculator.Compute(new RoutedPath(), Model, 1).TotalLossDb.ShouldBe(0);
    }

    [Fact]
    public void SampledQuarterCircle_IntegratesToTheArcRadiation()
    {
        const double radius = 8;
        var sampled = ConnectionLossCalculator.Compute(SampledQuarterCircle(radius), Model, 0);

        sampled.BendCount.ShouldBe(1);
        sampled.Bends[0].IsSampledCurve.ShouldBeTrue();
        sampled.Bends[0].RadiusMicrometers.ShouldBe(radius, 0.01);
        sampled.Bends[0].SweepDegrees.ShouldBe(90, 0.5);
        sampled.BendLossDb.ShouldBe(Model.RadiationLossDb(radius, 90), Model.RadiationLossDb(radius, 90) * 0.05);
        sampled.JunctionCount.ShouldBe(2, "inner vertex-to-vertex changes are adiabatic, only the ends count");
    }

    [Fact]
    public void SampledSBend_TighterCurveLosesMore()
    {
        var gentle = ConnectionLossCalculator.Compute(SampledSBend(length: 80, offset: 10), Model, 0);
        var tight = ConnectionLossCalculator.Compute(SampledSBend(length: 25, offset: 10), Model, 0);

        tight.MinBendRadiusMicrometers!.Value.ShouldBeLessThan(gentle.MinBendRadiusMicrometers!.Value);
        tight.BendLossDb.ShouldBeGreaterThan(gentle.BendLossDb);
        gentle.BendCount.ShouldBe(2, "an S-curve is two opposite bends, not 47 sampled kinks");
    }

    private static RoutedPath Path(params PathSegment[] segments)
    {
        var path = new RoutedPath();
        path.Segments.AddRange(segments);
        return path;
    }

    private static RoutedPath Corner(double radius) => Path(
        new StraightSegment(0, 0, 100, 0, 0),
        new BendSegment(100, radius, radius, 0, 90),
        new StraightSegment(100 + radius, radius, 100 + radius, 200, 90));

    private static RoutedPath SampledQuarterCircle(double radius)
    {
        var points = new List<(double X, double Y)> { (-20, 0) };
        for (int i = 0; i <= 48; i++)
        {
            double a = Math.PI / 2 * i / 48;
            points.Add((radius * Math.Sin(a), radius - radius * Math.Cos(a)));
        }
        points.Add((radius, radius + 20));
        return Path(CurvePolyline.ToSegments(points)!.ToArray());
    }

    private static RoutedPath SampledSBend(double length, double offset)
    {
        var points = new List<(double X, double Y)> { (-20, 0) };
        for (int i = 0; i <= 48; i++)
        {
            double x = length * i / 48;
            points.Add((x, offset / 2 * (1 - Math.Cos(Math.PI * x / length))));
        }
        points.Add((length + 20, offset));
        return Path(CurvePolyline.ToSegments(points)!.ToArray());
    }
}
