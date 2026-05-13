using System.Numerics;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using MathNetVector = MathNet.Numerics.LinearAlgebra.Vector<System.Numerics.Complex>;
using Shouldly;
using Xunit;

namespace UnitTests.Simulation.Reflection;

/// <summary>
/// Tests that the Neumann-series simulator (<see cref="SMatrix.CalcFieldAtPinsAfterStepsAsync"/>)
/// correctly propagates S11 and S22 back-reflected fields.
///
/// Audit context (issue #536): prior to this suite, all simulation tests used forward-only
/// S-matrices (S21/S12 only). These tests confirm that self-reflection elements (S11, S22)
/// do propagate through the iterative solver as expected.
/// </summary>
public class HighReflectionTransmissionTests
{
    // Tolerance for floating-point comparisons throughout this file.
    private const double Tolerance = 1e-9;

    /// <summary>
    /// Builds a 2-port S-matrix with explicit S11, S21, S12, S22 coefficients.
    /// Power conservation is NOT enforced here; test authors choose coefficients explicitly.
    /// </summary>
    private static (SMatrix sMatrix, Pin leftPin, Pin rightPin) BuildTwoPortMatrix(
        Complex s11, Complex s21, Complex s12, Complex s22)
    {
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
            { (leftPin.IDInFlow,  leftPin.IDOutFlow),  s11 },  // S11: left→left reflection
            { (leftPin.IDInFlow,  rightPin.IDOutFlow), s21 },  // S21: forward transmission
            { (rightPin.IDInFlow, leftPin.IDOutFlow),  s12 },  // S12: backward transmission
            { (rightPin.IDInFlow, rightPin.IDOutFlow), s22 },  // S22: right→right reflection
        });

        return (sMatrix, leftPin, rightPin);
    }

    /// <summary>
    /// Builds a unit-amplitude input vector at the specified pin.
    /// </summary>
    private static MathNetVector InputAt(SMatrix sMatrix, Guid pinId)
    {
        var vec = MathNet.Numerics.LinearAlgebra.Vector<Complex>.Build.Dense(sMatrix.PinReference.Count);
        vec[sMatrix.PinReference[pinId]] = Complex.One;
        return vec;
    }

    // -----------------------------------------------------------------------
    // S11 path: light injected at left port, reflected back from left port
    // -----------------------------------------------------------------------

    /// <summary>
    /// Verifies that S11 (left-port self-reflection) propagates non-zero field
    /// back to leftPin.IDOutFlow after the Neumann series iteration.
    /// This is the minimal "reflection works" smoke test.
    /// </summary>
    [Fact]
    public async Task S11_ReflectedFieldIsNonZeroAtSourcePin()
    {
        // 90 % power reflection, 10 % transmission (lossless 2-port)
        const double powerReflection = 0.9;
        double r = Math.Sqrt(powerReflection);
        double t = Math.Sqrt(1.0 - powerReflection);

        var (sMatrix, leftPin, _) = BuildTwoPortMatrix(
            s11: new Complex(r, 0),
            s21: new Complex(t, 0),
            s12: new Complex(t, 0),
            s22: new Complex(r, 0));

        var inputVec = InputAt(sMatrix, leftPin.IDInFlow);
        int stepCount = sMatrix.PinReference.Count * 2;
        using var cts = new CancellationTokenSource();

        var result = await sMatrix.CalcFieldAtPinsAfterStepsAsync(inputVec, stepCount, cts);

        result[leftPin.IDOutFlow].Magnitude.ShouldBeGreaterThan(
            0, "S11 reflected field must be non-zero at left IDOutFlow");
    }

    /// <summary>
    /// Verifies that the reflected amplitude at leftPin.IDOutFlow matches the S11 coefficient
    /// precisely.  The Neumann series converges in one step for a single component with no
    /// feedback loop, so the exact analytic result is achievable.
    /// </summary>
    [Fact]
    public async Task S11_ReflectedAmplitudeMatchesCoefficient()
    {
        const double powerReflection = 0.9;
        double r = Math.Sqrt(powerReflection);
        double t = Math.Sqrt(1.0 - powerReflection);

        var (sMatrix, leftPin, rightPin) = BuildTwoPortMatrix(
            s11: new Complex(r, 0),
            s21: new Complex(t, 0),
            s12: new Complex(t, 0),
            s22: new Complex(r, 0));

        var inputVec = InputAt(sMatrix, leftPin.IDInFlow);
        int stepCount = sMatrix.PinReference.Count * 2;
        using var cts = new CancellationTokenSource();

        var result = await sMatrix.CalcFieldAtPinsAfterStepsAsync(inputVec, stepCount, cts);

        // Reflected field equals S11 amplitude coefficient
        result[leftPin.IDOutFlow].Magnitude.ShouldBe(r, Tolerance,
            "Reflected amplitude must equal the S11 coefficient exactly");

        // Transmitted field equals S21 amplitude coefficient
        result[rightPin.IDOutFlow].Magnitude.ShouldBe(t, Tolerance,
            "Transmitted amplitude must equal the S21 coefficient exactly");
    }

    /// <summary>
    /// Verifies that power is conserved: reflected power + transmitted power = input power.
    /// Confirms that the Neumann series does not introduce spurious gain or loss for
    /// a lossless S-matrix with r² + t² = 1.
    /// </summary>
    [Fact]
    public async Task PowerIsConservedAcrossHighReflectionComponent()
    {
        const double powerReflection = 0.9;
        double r = Math.Sqrt(powerReflection);
        double t = Math.Sqrt(1.0 - powerReflection);

        var (sMatrix, leftPin, rightPin) = BuildTwoPortMatrix(
            s11: new Complex(r, 0),
            s21: new Complex(t, 0),
            s12: new Complex(t, 0),
            s22: new Complex(r, 0));

        var inputVec = InputAt(sMatrix, leftPin.IDInFlow);
        int stepCount = sMatrix.PinReference.Count * 2;
        using var cts = new CancellationTokenSource();

        var result = await sMatrix.CalcFieldAtPinsAfterStepsAsync(inputVec, stepCount, cts);

        double reflectedPower  = Math.Pow(result[leftPin.IDOutFlow].Magnitude, 2);
        double transmittedPower = Math.Pow(result[rightPin.IDOutFlow].Magnitude, 2);
        double totalOutputPower = reflectedPower + transmittedPower;

        totalOutputPower.ShouldBe(1.0, Tolerance,
            "Total output power (reflected + transmitted) must equal input power for a lossless component");
    }

    // -----------------------------------------------------------------------
    // S22 path: light injected at right port, reflected back from right port
    // -----------------------------------------------------------------------

    /// <summary>
    /// Verifies that S22 (right-port self-reflection) propagates correctly when
    /// light is injected from the right side.  Symmetric with the S11 test above.
    /// </summary>
    [Fact]
    public async Task S22_ReflectedAmplitudeMatchesCoefficientWhenInjectedFromRight()
    {
        const double powerReflection = 0.9;
        double r = Math.Sqrt(powerReflection);
        double t = Math.Sqrt(1.0 - powerReflection);

        var (sMatrix, leftPin, rightPin) = BuildTwoPortMatrix(
            s11: new Complex(r, 0),
            s21: new Complex(t, 0),
            s12: new Complex(t, 0),
            s22: new Complex(r, 0));

        // Inject at RIGHT port
        var inputVec = InputAt(sMatrix, rightPin.IDInFlow);
        int stepCount = sMatrix.PinReference.Count * 2;
        using var cts = new CancellationTokenSource();

        var result = await sMatrix.CalcFieldAtPinsAfterStepsAsync(inputVec, stepCount, cts);

        result[rightPin.IDOutFlow].Magnitude.ShouldBe(r, Tolerance,
            "S22 reflected amplitude must equal the S22 coefficient");

        result[leftPin.IDOutFlow].Magnitude.ShouldBe(t, Tolerance,
            "Backward (S12) transmitted amplitude must equal the S12 coefficient");
    }

    // -----------------------------------------------------------------------
    // Convergence audit
    // -----------------------------------------------------------------------

    /// <summary>
    /// Confirms that the step-count heuristic (pinCount × 2) converges to the
    /// steady-state within floating-point precision for a single component.
    ///
    /// A single 2-port component with no downstream feedback has no cavity
    /// resonance — the Neumann series converges in exactly one step.  This
    /// result establishes a baseline that the heuristic is at least sufficient
    /// for non-resonant circuits.
    /// </summary>
    [Fact]
    public async Task NeummanSeries_ConvergesInOneStep_ForSingleNonResonantComponent()
    {
        double r = Math.Sqrt(0.5);
        double t = Math.Sqrt(0.5);

        var (sMatrix, leftPin, rightPin) = BuildTwoPortMatrix(
            s11: new Complex(r, 0),
            s21: new Complex(t, 0),
            s12: new Complex(t, 0),
            s22: new Complex(r, 0));

        var inputVec = InputAt(sMatrix, leftPin.IDInFlow);
        using var cts = new CancellationTokenSource();

        // Run with 1 step
        var resultOneStep = await sMatrix.CalcFieldAtPinsAfterStepsAsync(inputVec, 1, cts);
        // Run with 100 steps
        var resultManySteps = await sMatrix.CalcFieldAtPinsAfterStepsAsync(inputVec, 100, cts);

        double diff = Math.Abs(
            resultOneStep[rightPin.IDOutFlow].Magnitude -
            resultManySteps[rightPin.IDOutFlow].Magnitude);

        diff.ShouldBeLessThan(Tolerance,
            "For a non-resonant component, 1 step and 100 steps must yield identical results — " +
            "the Neumann series must have converged in exactly one iteration.");
    }
}
