using System.Numerics;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Simulation.Reflection;

/// <summary>
/// Tests Fabry-Pérot resonance via the Neumann-series solver.
/// Circuit: LaserSource → Reflector1 (S11=r, S21=t) → Waveguide (phase φ) → Reflector2.
/// Analytic: T_FP = t₁t₂e^{iφ} / (1−r₁r₂e^{2iφ}).
/// Convergence note: with r=0.3 and pinCount×2=24 steps, truncation error ≈ 7×10⁻⁴.
/// For r > 0.8 the heuristic is insufficient — see issue #536 follow-up.
/// </summary>
public class FabryPerotResonanceTests
{
    // Moderate reflectivity chosen so the default step heuristic (pinCount × 2) converges.
    private const double ReflectorR = 0.3;
    private const double ReflectorT = 0.954; // √(1 − 0.09) ≈ 0.9539

    // 2 % tolerance on analytic predictions; accounts for Neumann-series truncation error.
    private const double AnalyticTolerance = 0.02;

    // -----------------------------------------------------------------------
    // Circuit builder helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a partial reflector (symmetric, lossless) with amplitude reflection r
    /// and amplitude transmission t = √(1−r²).
    /// Returns (sMatrix, leftPin, rightPin).
    /// </summary>
    private static (SMatrix sMatrix, Pin leftPin, Pin rightPin) CreateReflector(double r)
    {
        double t = Math.Sqrt(1.0 - r * r);
        var leftPin = new Pin("left", 0, MatterType.Light, RectSide.Left);
        var rightPin = new Pin("right", 1, MatterType.Light, RectSide.Right);

        var allPinIds = new List<Guid>
        {
            leftPin.IDInFlow, leftPin.IDOutFlow,
            rightPin.IDInFlow, rightPin.IDOutFlow
        };

        var sMatrix = new SMatrix(allPinIds, new());
        sMatrix.SetValues(new()
        {
            { (leftPin.IDInFlow,  leftPin.IDOutFlow),  new Complex(r, 0) },  // S11
            { (leftPin.IDInFlow,  rightPin.IDOutFlow), new Complex(t, 0) },  // S21
            { (rightPin.IDInFlow, leftPin.IDOutFlow),  new Complex(t, 0) },  // S12
            { (rightPin.IDInFlow, rightPin.IDOutFlow), new Complex(r, 0) },  // S22
        });

        return (sMatrix, leftPin, rightPin);
    }

    /// <summary>
    /// Creates a lossless phase-delay waveguide with single-pass phase φ (radians).
    /// S21 = S12 = e^{iφ}, no reflection terms.
    /// </summary>
    private static (SMatrix sMatrix, Pin leftPin, Pin rightPin) CreatePhaseDelay(double phiRadians)
    {
        var leftPin = new Pin("wg_left", 0, MatterType.Light, RectSide.Left);
        var rightPin = new Pin("wg_right", 1, MatterType.Light, RectSide.Right);

        var allPinIds = new List<Guid>
        {
            leftPin.IDInFlow, leftPin.IDOutFlow,
            rightPin.IDInFlow, rightPin.IDOutFlow
        };

        var phaseCoeff = Complex.FromPolarCoordinates(1.0, phiRadians);
        var sMatrix = new SMatrix(allPinIds, new());
        sMatrix.SetValues(new()
        {
            { (leftPin.IDInFlow,  rightPin.IDOutFlow), phaseCoeff },   // S21
            { (rightPin.IDInFlow, leftPin.IDOutFlow),  phaseCoeff },   // S12
        });

        return (sMatrix, leftPin, rightPin);
    }

    /// <summary>
    /// Creates a 4-pin bidirectional connection S-matrix between two pins.
    /// Encodes both directions: outFlow1 → inFlow2 and outFlow2 → inFlow1.
    /// </summary>
    private static SMatrix CreateConnectionMatrix(
        Guid outFlow1, Guid inFlow2,
        Guid outFlow2, Guid inFlow1)
    {
        var connPins = new List<Guid> { outFlow1, inFlow2, outFlow2, inFlow1 };
        var connMatrix = new SMatrix(connPins.Distinct().ToList(), new());
        connMatrix.SetValues(new()
        {
            { (outFlow1, inFlow2), Complex.One },
            { (outFlow2, inFlow1), Complex.One },
        });
        return connMatrix;
    }

    /// <summary>
    /// Assembles the FP system matrix and input vector, runs the simulation, and
    /// returns the field magnitude at Reflector2's right output (the "transmitted" port).
    ///
    /// Circuit:  [input] → Reflector1 → Waveguide(φ) → Reflector2 → [measured]
    /// </summary>
    private static async Task<double> RunFabryPerotAsync(double phiRadians, double r = ReflectorR)
    {
        var (r1Matrix, r1Left, r1Right) = CreateReflector(r);
        var (wgMatrix, wgLeft, wgRight)  = CreatePhaseDelay(phiRadians);
        var (r2Matrix, r2Left, r2Right) = CreateReflector(r);

        // Connections: R1.right ↔ WG.left,  WG.right ↔ R2.left
        var conn1 = CreateConnectionMatrix(
            r1Right.IDOutFlow, wgLeft.IDInFlow,
            wgLeft.IDOutFlow,  r1Right.IDInFlow);

        var conn2 = CreateConnectionMatrix(
            wgRight.IDOutFlow, r2Left.IDInFlow,
            r2Left.IDOutFlow,  wgRight.IDInFlow);

        var systemMatrix = SMatrix.CreateSystemSMatrix(
            new List<SMatrix> { r1Matrix, wgMatrix, r2Matrix, conn1, conn2 });

        // Input: unit amplitude at the left port of Reflector1
        var inputVec = MathNet.Numerics.LinearAlgebra.Vector<Complex>.Build.Dense(systemMatrix.PinReference.Count);
        inputVec[systemMatrix.PinReference[r1Left.IDInFlow]] = Complex.One;

        int stepCount = systemMatrix.PinReference.Count * 2;
        using var cts = new CancellationTokenSource();

        var result = await systemMatrix.CalcFieldAtPinsAfterStepsAsync(inputVec, stepCount, cts);
        return result[r2Right.IDOutFlow].Magnitude;
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resonance (φ=0) must show higher transmission than anti-resonance (φ=π/2).
    /// Analytic ratio: (1+r₁r₂)/(1−r₁r₂) ≈ 1.20 for r=0.3.
    /// </summary>
    [Fact]
    public async Task FabryPerot_ResonanceTransmissionExceedsAntiResonance()
    {
        double transmissionAtResonance    = await RunFabryPerotAsync(phiRadians: 0.0);
        double transmissionAtAntiResonance = await RunFabryPerotAsync(phiRadians: Math.PI / 2);

        transmissionAtResonance.ShouldBeGreaterThan(transmissionAtAntiResonance,
            "Fabry-Pérot cavity must show higher transmission at resonance than at anti-resonance. " +
            "If this fails, S11/S22 back-reflection is not being propagated by the Neumann series.");
    }

    /// <summary>
    /// Resonance transmission |T_res|² = (t₁t₂)²/(1−r₁r₂)² = 1 for a symmetric lossless FP.
    /// </summary>
    [Fact]
    public async Task FabryPerot_ResonanceTransmissionApproachesAnalyticValue()
    {
        double t = Math.Sqrt(1.0 - ReflectorR * ReflectorR);
        double analyticTMax = (t * t) / (1.0 - ReflectorR * ReflectorR); // = t²/(t²) = 1.0

        double measured = await RunFabryPerotAsync(phiRadians: 0.0);

        measured.ShouldBe(analyticTMax, AnalyticTolerance,
            $"FP resonance transmission should be ≈ {analyticTMax:F3} (analytic). " +
            $"Measured: {measured:F3}. Tolerance: {AnalyticTolerance}.");
    }

    /// <summary>Anti-resonance (φ=π/2): |T_antires| = t₁t₂/(1+r₁r₂).</summary>
    [Fact]
    public async Task FabryPerot_AntiResonanceTransmissionApproachesAnalyticValue()
    {
        double t = Math.Sqrt(1.0 - ReflectorR * ReflectorR);
        double analyticTMin = (t * t) / (1.0 + ReflectorR * ReflectorR); // (1-r²)/(1+r²)

        double measured = await RunFabryPerotAsync(phiRadians: Math.PI / 2);

        measured.ShouldBe(analyticTMin, AnalyticTolerance,
            $"FP anti-resonance transmission should be ≈ {analyticTMin:F3} (analytic). " +
            $"Measured: {measured:F3}. Tolerance: {AnalyticTolerance}.");
    }

    /// <summary>
    /// Confirms pinCount×2 heuristic convergence for r=0.3 (round-trip factor 0.09).
    /// KNOWN GAP: for r > 0.8 the heuristic under-converges; a residual-based loop
    /// would be needed — filed as a follow-up to issue #536.
    /// </summary>
    [Fact]
    public async Task StepCountHeuristic_IsSufficientForModerateReflectivity()
    {
        // Run with exactly pinCount×2 steps (the heuristic)
        double transmissionHeuristic = await RunFabryPerotAsync(phiRadians: 0.0, r: ReflectorR);

        // Run with 4× as many steps (should be fully converged)
        double transmissionDeep;
        {
            var (r1Matrix, r1Left, r1Right) = CreateReflector(ReflectorR);
            var (wgMatrix, wgLeft, wgRight)  = CreatePhaseDelay(0.0);
            var (r2Matrix, r2Left, r2Right) = CreateReflector(ReflectorR);

            var conn1 = CreateConnectionMatrix(
                r1Right.IDOutFlow, wgLeft.IDInFlow,
                wgLeft.IDOutFlow,  r1Right.IDInFlow);
            var conn2 = CreateConnectionMatrix(
                wgRight.IDOutFlow, r2Left.IDInFlow,
                r2Left.IDOutFlow,  wgRight.IDInFlow);

            var sys = SMatrix.CreateSystemSMatrix(
                new List<SMatrix> { r1Matrix, wgMatrix, r2Matrix, conn1, conn2 });

            var inputVec = MathNet.Numerics.LinearAlgebra.Vector<Complex>.Build.Dense(sys.PinReference.Count);
            inputVec[sys.PinReference[r1Left.IDInFlow]] = Complex.One;

            using var cts = new CancellationTokenSource();
            // 4× heuristic = fully converged reference
            var result = await sys.CalcFieldAtPinsAfterStepsAsync(inputVec, sys.PinReference.Count * 8, cts);
            transmissionDeep = result[r2Right.IDOutFlow].Magnitude;
        }

        double relativeError = Math.Abs(transmissionHeuristic - transmissionDeep) / transmissionDeep;

        relativeError.ShouldBeLessThan(0.01,
            $"For r = {ReflectorR} (moderate reflectivity) the pinCount×2 heuristic must agree " +
            $"with the fully converged result within 1 %. " +
            $"Heuristic: {transmissionHeuristic:F4}, Deep: {transmissionDeep:F4}, " +
            $"RelError: {relativeError:P1}. " +
            $"KNOWN GAP: for r > 0.8 this test would fail — see issue #536 follow-up.");
    }
}
