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

        // t = 0.25. The lower endpoint has zero magnitude (undefined phase), so the
        // polar lerp takes the upper endpoint's phase: 0.25 · e^{iπ/2} = 0.25 i.
        result.SMat[0, 1].Magnitude.ShouldBe(0.25, 1e-10);
        result.SMat[0, 1].Phase.ShouldBe(Math.PI / 2, 1e-10);
    }

    // ── polar interpolation (chord-attenuation regression, #1359 step 0) ───────

    [Fact]
    public void GetMatrix_RotatingPhasor_KeepsMagnitudeNoChordAttenuation()
    {
        // A lossless entry whose phase rotates 58° between stops (the EBeam halfring
        // arc over one 10 nm PDK interval). Cartesian lerp would cut the chord and
        // drop the midpoint magnitude to cos(29°) ≈ 0.875; polar lerp must keep 1.0.
        var pinA = Guid.NewGuid();
        var pinB = Guid.NewGuid();
        var lo = CreateMatrix(pinA, pinB, Complex.FromPolarCoordinates(1.0, 0));
        var hi = CreateMatrix(pinA, pinB, Complex.FromPolarCoordinates(1.0, 58.0 * Math.PI / 180.0));
        var map = new Dictionary<int, SMatrix> { { 1550, lo }, { 1560, hi } };

        var result = WavelengthInterpolator.GetMatrix(map, 1555.0, out _);

        result.SMat[0, 1].Magnitude.ShouldBe(1.0, 1e-10);
        result.SMat[0, 1].Phase.ShouldBe(29.0 * Math.PI / 180.0, 1e-10);
    }

    [Fact]
    public void GetMatrix_PhaseCrossingPlusMinusPi_TakesShortUnwrappedArc()
    {
        // 170° → −170° must interpolate through ±180°, not backwards through 0°.
        var pinA = Guid.NewGuid();
        var pinB = Guid.NewGuid();
        var lo = CreateMatrix(pinA, pinB, Complex.FromPolarCoordinates(0.5, 170.0 * Math.PI / 180.0));
        var hi = CreateMatrix(pinA, pinB, Complex.FromPolarCoordinates(0.5, -170.0 * Math.PI / 180.0));
        var map = new Dictionary<int, SMatrix> { { 1550, lo }, { 1560, hi } };

        var result = WavelengthInterpolator.GetMatrix(map, 1555.0, out _);

        result.SMat[0, 1].Magnitude.ShouldBe(0.5, 1e-10);
        Math.Abs(result.SMat[0, 1].Phase).ShouldBe(Math.PI, 1e-6);
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

    // ── sub-nm (double) targets — the ONA sweep grid (#1349) ───────────────────

    [Fact]
    public void GetMatrix_IntegralDoubleTarget_ReturnsSameInstanceAsIntPath()
    {
        var pinA = Guid.NewGuid();
        var pinB = Guid.NewGuid();
        var lo = CreateMatrix(pinA, pinB, new Complex(0.3, 0));
        var hi = CreateMatrix(pinA, pinB, new Complex(0.9, 0));
        var map = new Dictionary<int, SMatrix> { { 1500, lo }, { 1600, hi } };

        var result = WavelengthInterpolator.GetMatrix(map, 1500.0, out bool wasInterpolated);

        result.ShouldBeSameAs(lo,
            "an integral double must hit the exact stop, not interpolate — " +
            "this keeps all-integer sweeps bit-identical to the old integer grid");
        wasInterpolated.ShouldBeFalse();
    }

    [Fact]
    public void GetMatrix_FractionalTarget_InterpolatesBetweenBracketingStops()
    {
        var pinA = Guid.NewGuid();
        var pinB = Guid.NewGuid();
        var lo = CreateMatrix(pinA, pinB, new Complex(0.0, 0));
        var hi = CreateMatrix(pinA, pinB, new Complex(1.0, 0));
        var map = new Dictionary<int, SMatrix> { { 1550, lo }, { 1551, hi } };

        var result = WavelengthInterpolator.GetMatrix(map, 1550.25, out bool wasInterpolated);

        wasInterpolated.ShouldBeTrue();
        result.SMat[0, 1].Real.ShouldBe(0.25, 1e-10);
    }

    [Fact]
    public void GetMatrix_FractionalTargetOutsideRange_FallsBackToNearestStop()
    {
        var pinA = Guid.NewGuid();
        var pinB = Guid.NewGuid();
        var lo = CreateMatrix(pinA, pinB, new Complex(0.3, 0));
        var hi = CreateMatrix(pinA, pinB, new Complex(0.9, 0));
        var map = new Dictionary<int, SMatrix> { { 1500, lo }, { 1600, hi } };

        var result = WavelengthInterpolator.GetMatrix(map, 1499.7, out bool wasInterpolated);

        result.ShouldBeSameAs(lo);
        wasInterpolated.ShouldBeFalse();
    }
}
