using CAP_Core.LightCalculation.MaterialDispersion;
using Shouldly;
using Xunit;

namespace UnitTests.LightCalculation.MaterialDispersion;

/// <summary>
/// Tests for <see cref="TabulatedDispersion"/>.
/// Covers interpolation, clamping, and the acceptance criterion:
/// "Waveguide loss at 1500 nm differs from loss at 1600 nm when a tabulated model is provided."
/// </summary>
public class TabulatedDispersionTests
{
    private static readonly (double, double)[] NEffPts = [(1500, 2.50), (1550, 2.45), (1600, 2.40)];
    private static readonly (double, double)[] LossPts = [(1500, 0.7), (1550, 0.5), (1600, 0.4)];

    [Fact]
    public void LossAt_DiffersAcross100nm_AcceptanceCriterion()
    {
        var model = new TabulatedDispersion(NEffPts, null, LossPts);

        double loss1500 = model.LossDbPerCmAt(1500.0);
        double loss1600 = model.LossDbPerCmAt(1600.0);

        // Key acceptance criterion: loss must differ between the two wavelengths
        loss1500.ShouldNotBe(loss1600);
        loss1500.ShouldBe(0.7, tolerance: 1e-12);
        loss1600.ShouldBe(0.4, tolerance: 1e-12);
    }

    [Fact]
    public void LossAt_Midpoint_LinearlyInterpolated()
    {
        var model = new TabulatedDispersion(NEffPts, null, LossPts);

        // Midpoint 1525 nm: between 1500→0.7 and 1550→0.5
        // t = (1525-1500)/(1550-1500) = 0.5  → loss = 0.7 + 0.5*(0.5-0.7) = 0.6
        model.LossDbPerCmAt(1525.0).ShouldBe(0.6, tolerance: 1e-10);
    }

    [Fact]
    public void NEffAt_ExactStops_ReturnsExactValues()
    {
        var model = new TabulatedDispersion(NEffPts, null, LossPts);
        model.NEffAt(1500.0).ShouldBe(2.50, tolerance: 1e-12);
        model.NEffAt(1550.0).ShouldBe(2.45, tolerance: 1e-12);
        model.NEffAt(1600.0).ShouldBe(2.40, tolerance: 1e-12);
    }

    [Fact]
    public void NEffAt_BelowRange_ClampsToFirstPoint()
    {
        var model = new TabulatedDispersion(NEffPts, null, LossPts);
        model.NEffAt(1400.0).ShouldBe(2.50, tolerance: 1e-12);
    }

    [Fact]
    public void NEffAt_AboveRange_ClampsToLastPoint()
    {
        var model = new TabulatedDispersion(NEffPts, null, LossPts);
        model.NEffAt(1700.0).ShouldBe(2.40, tolerance: 1e-12);
    }

    [Fact]
    public void GroupIndexAt_WithExplicitPoints_UsesProvided()
    {
        (double, double)[] ngPts = [(1550, 4.2)];
        var model = new TabulatedDispersion(NEffPts, ngPts, LossPts);
        model.GroupIndexAt(1550.0).ShouldBe(4.2, tolerance: 1e-12);
    }

    [Fact]
    public void GroupIndexAt_WithoutExplicitPoints_DerivedFromNEffSlope()
    {
        var model = new TabulatedDispersion(NEffPts, null, LossPts);
        // Should not throw and should return a positive value (physics sanity)
        double ng = model.GroupIndexAt(1550.0);
        ng.ShouldBeGreaterThan(0.0);
    }

    [Fact]
    public void Constructor_EmptyNEffPoints_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() =>
            new TabulatedDispersion(Array.Empty<(double, double)>(), null, LossPts));
    }

    [Fact]
    public void Constructor_EmptyLossPoints_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() =>
            new TabulatedDispersion(NEffPts, null, Array.Empty<(double, double)>()));
    }

    [Fact]
    public void LossAt_NeverNegative_WhenAllPointsPositive()
    {
        var model = new TabulatedDispersion(NEffPts, null, LossPts);
        for (double wl = 1400; wl <= 1700; wl += 10)
            model.LossDbPerCmAt(wl).ShouldBeGreaterThanOrEqualTo(0.0);
    }
}
