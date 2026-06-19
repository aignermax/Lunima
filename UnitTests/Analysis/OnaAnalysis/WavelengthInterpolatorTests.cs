using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.LightCalculation;
using Shouldly;
using System.Numerics;
using Xunit;

namespace UnitTests.Analysis.OnaAnalysis;

public class WavelengthInterpolatorTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private static SMatrix CreateMatrix(Guid pinA, Guid pinB, Complex transmission)
    {
        var pins = new List<Guid> { pinA, pinB };
        var sliders = new List<(Guid, double)>();
        var m = new SMatrix(pins, sliders);
        m.SMat[0, 1] = transmission; // pinB-in → pinA-out
        m.SMat[1, 0] = transmission;
        return m;
    }

    // ── exact match ────────────────────────────────────────────────────────────

    [Fact]
    public void GetMatrix_ExactMatch_ReturnsSameInstance()
    {
        var pinA = Guid.NewGuid();
        var pinB = Guid.NewGuid();
        var matrix = CreateMatrix(pinA, pinB, new Complex(0.9, 0));
        var map = new Dictionary<int, SMatrix> { { 1550, matrix } };

        var result = WavelengthInterpolator.GetMatrix(map, 1550, out bool wasInterpolated);

        result.ShouldBeSameAs(matrix);
        wasInterpolated.ShouldBeFalse();
    }

    // ── interpolation between stops ────────────────────────────────────────────

    [Fact]
    public void GetMatrix_TargetBetweenStops_InterpolatesLinearlyMidpoint()
    {
        var pinA = Guid.NewGuid();
        var pinB = Guid.NewGuid();
        var lo = CreateMatrix(pinA, pinB, new Complex(0.0, 0));
        var hi = CreateMatrix(pinA, pinB, new Complex(1.0, 0));
        var map = new Dictionary<int, SMatrix> { { 1500, lo }, { 1600, hi } };

        var result = WavelengthInterpolator.GetMatrix(map, 1550, out bool wasInterpolated);

        wasInterpolated.ShouldBeTrue();
        result.SMat[0, 1].Real.ShouldBe(0.5, 1e-10);
    }

    [Fact]
    public void GetMatrix_InterpolatesImaginaryPartCorrectly()
    {
        var pinA = Guid.NewGuid();
        var pinB = Guid.NewGuid();
        var lo = CreateMatrix(pinA, pinB, new Complex(0, 0));
        var hi = CreateMatrix(pinA, pinB, new Complex(0, 1));
        var map = new Dictionary<int, SMatrix> { { 1500, lo }, { 1600, hi } };

        var result = WavelengthInterpolator.GetMatrix(map, 1525, out _);

        // t = (1525-1500)/(1600-1500) = 0.25
        result.SMat[0, 1].Imaginary.ShouldBe(0.25, 1e-10);
    }

    [Fact]
    public void GetMatrix_TargetAtLowerStop_ReturnsExact()
    {
        var pinA = Guid.NewGuid();
        var pinB = Guid.NewGuid();
        var lo = CreateMatrix(pinA, pinB, new Complex(0.3, 0));
        var hi = CreateMatrix(pinA, pinB, new Complex(0.9, 0));
        var map = new Dictionary<int, SMatrix> { { 1500, lo }, { 1600, hi } };

        var result = WavelengthInterpolator.GetMatrix(map, 1500, out bool wasInterpolated);

        result.ShouldBeSameAs(lo);
        wasInterpolated.ShouldBeFalse();
    }

    // ── extrapolation (nearest-neighbour) ──────────────────────────────────────

    [Fact]
    public void GetMatrix_TargetBelowAllStops_FallsBackToLowest()
    {
        var pinA = Guid.NewGuid();
        var pinB = Guid.NewGuid();
        var lo = CreateMatrix(pinA, pinB, new Complex(0.3, 0));
        var hi = CreateMatrix(pinA, pinB, new Complex(0.9, 0));
        var map = new Dictionary<int, SMatrix> { { 1500, lo }, { 1600, hi } };

        var result = WavelengthInterpolator.GetMatrix(map, 1400, out bool wasInterpolated);

        result.ShouldBeSameAs(lo);
        wasInterpolated.ShouldBeFalse();
    }

    [Fact]
    public void GetMatrix_TargetAboveAllStops_FallsBackToHighest()
    {
        var pinA = Guid.NewGuid();
        var pinB = Guid.NewGuid();
        var lo = CreateMatrix(pinA, pinB, new Complex(0.3, 0));
        var hi = CreateMatrix(pinA, pinB, new Complex(0.9, 0));
        var map = new Dictionary<int, SMatrix> { { 1500, lo }, { 1600, hi } };

        var result = WavelengthInterpolator.GetMatrix(map, 1700, out bool wasInterpolated);

        result.ShouldBeSameAs(hi);
        wasInterpolated.ShouldBeFalse();
    }

    // ── single stop (only nearest-neighbour available) ─────────────────────────

    [Fact]
    public void GetMatrix_SingleStop_AlwaysReturnsThatStop()
    {
        var pinA = Guid.NewGuid();
        var pinB = Guid.NewGuid();
        var only = CreateMatrix(pinA, pinB, new Complex(0.7, 0));
        var map = new Dictionary<int, SMatrix> { { 1550, only } };

        var result = WavelengthInterpolator.GetMatrix(map, 1525, out bool wasInterpolated);

        result.ShouldBeSameAs(only);
        wasInterpolated.ShouldBeFalse();
    }
}
