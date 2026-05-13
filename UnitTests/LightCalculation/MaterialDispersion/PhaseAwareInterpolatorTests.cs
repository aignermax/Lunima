using System.Numerics;
using CAP_Core.LightCalculation.MaterialDispersion;
using Shouldly;
using Xunit;

namespace UnitTests.LightCalculation.MaterialDispersion;

/// <summary>
/// Tests for <see cref="PhaseAwareInterpolator"/>.
/// Acceptance criterion: a unit test fails the naïve linear interpolator on a
/// resonance crossing (phase jump from ~π to ~−π).
/// </summary>
public class PhaseAwareInterpolatorTests
{
    private readonly PhaseAwareInterpolator _interpolator = new();

    // Helper to build a wavelength stop
    private static (double WavelengthNm, IReadOnlyList<(string, string, Complex)> Connections) Stop(
        double wl, Complex value)
        => (wl, new List<(string, string, Complex)> { ("in", "out", value) });

    [Fact]
    public void Interpolate_SingleStop_ReturnsThatStop()
    {
        var s = Complex.FromPolarCoordinates(0.9, 0.5);
        var stops = new[] { Stop(1550, s) };

        var result = _interpolator.Interpolate(stops, 1550);

        result[("in", "out")].Magnitude.ShouldBe(0.9, tolerance: 1e-10);
        result[("in", "out")].Phase.ShouldBe(0.5, tolerance: 1e-10);
    }

    [Fact]
    public void Interpolate_BelowRange_ReturnsFirstStop()
    {
        var stops = new[]
        {
            Stop(1500, Complex.FromPolarCoordinates(0.8, 0.1)),
            Stop(1600, Complex.FromPolarCoordinates(0.7, 0.2)),
        };

        var result = _interpolator.Interpolate(stops, 1400);
        result[("in", "out")].Magnitude.ShouldBe(0.8, tolerance: 1e-10);
    }

    [Fact]
    public void Interpolate_AboveRange_ReturnsLastStop()
    {
        var stops = new[]
        {
            Stop(1500, Complex.FromPolarCoordinates(0.8, 0.1)),
            Stop(1600, Complex.FromPolarCoordinates(0.7, 0.2)),
        };

        var result = _interpolator.Interpolate(stops, 1700);
        result[("in", "out")].Magnitude.ShouldBe(0.7, tolerance: 1e-10);
    }

    [Fact]
    public void Interpolate_Midpoint_LogMagnitudeInterpolated()
    {
        // mag at 1500: 0.1,  mag at 1600: 0.01
        // Log-mag midpoint: exp( (ln(0.1)+ln(0.01))/2 ) = exp(-2.302 + -4.605)/2 = exp(-3.454) ≈ 0.0316
        var stops = new[]
        {
            Stop(1500, Complex.FromPolarCoordinates(0.1, 0.0)),
            Stop(1600, Complex.FromPolarCoordinates(0.01, 0.0)),
        };

        var result = _interpolator.Interpolate(stops, 1550);
        double expectedLogMag = Math.Sqrt(0.1 * 0.01); // geometric mean = exp of arithmetic mean of logs
        result[("in", "out")].Magnitude.ShouldBe(expectedLogMag, tolerance: 1e-6);
    }

    /// <summary>
    /// ACCEPTANCE CRITERION: Naïve linear interpolation fails at a resonance phase jump
    /// (e.g. phase going from π−ε to −π+ε, which crosses zero phase when properly unwrapped).
    /// The phase-aware interpolator must produce a result near π, not near 0.
    /// </summary>
    [Fact]
    public void Interpolate_ResonancePhaseJump_NaiveLinearWouldFail()
    {
        // Simulate a resonance crossing: phase goes from just below +π to just above -π
        double phaseNearPlusPI = Math.PI - 0.05;   // ≈ +3.09 rad
        double phaseNearMinusPI = -Math.PI + 0.05; // ≈ -3.09 rad

        var stops = new[]
        {
            Stop(1549, Complex.FromPolarCoordinates(0.5, phaseNearPlusPI)),
            Stop(1551, Complex.FromPolarCoordinates(0.5, phaseNearMinusPI)),
        };

        var result = _interpolator.Interpolate(stops, 1550);
        double interpPhase = result[("in", "out")].Phase;

        // Phase-aware: the two phases are separated by only 0.1 rad (after unwrap),
        // so the midpoint phase should be near ±π, not near 0.
        // Naïve linear interpolation would give (3.09 + (-3.09))/2 = 0 — clearly wrong.
        double naiveLinear = (phaseNearPlusPI + phaseNearMinusPI) / 2.0; // ≈ 0
        naiveLinear.ShouldBe(0.0, tolerance: 0.01); // verify the naïve answer is indeed ~0

        // The phase-aware answer should be near ±π (the correct midpoint)
        double absPhase = Math.Abs(interpPhase);
        absPhase.ShouldBeGreaterThan(Math.PI - 0.1, "Phase-aware interpolator should be near ±π at resonance");
    }

    [Fact]
    public void InterpolateSParameter_Midpoint_CorrectMagnitudeAndPhase()
    {
        var s0 = Complex.FromPolarCoordinates(1.0, 0.0);
        var s1 = Complex.FromPolarCoordinates(1.0, Math.PI / 2);

        var result = PhaseAwareInterpolator.InterpolateSParameter(s0, s1, 0.5);

        result.Magnitude.ShouldBe(1.0, tolerance: 1e-10);
        result.Phase.ShouldBe(Math.PI / 4, tolerance: 1e-10);
    }

    [Fact]
    public void Interpolate_EmptyStops_ReturnsEmptyDictionary()
    {
        var result = _interpolator.Interpolate(
            Array.Empty<(double, IReadOnlyList<(string, string, Complex)>)>(),
            1550);
        result.ShouldBeEmpty();
    }
}
