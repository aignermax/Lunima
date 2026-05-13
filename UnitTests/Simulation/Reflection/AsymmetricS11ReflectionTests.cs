using System.Numerics;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using MathNetVector = MathNet.Numerics.LinearAlgebra.Vector<System.Numerics.Complex>;
using Shouldly;
using Xunit;

namespace UnitTests.Simulation.Reflection;

/// <summary>
/// Tests for asymmetric S-matrix elements where S11 ≠ S22.
///
/// Physical examples: isolators, Sagnac loops, circulators and absorber-terminated
/// waveguides all have S11 ≠ S22.  The simulator must honour each coefficient
/// independently rather than aliasing them.
///
/// Audit context (issue #536): confirms directionality is preserved — light
/// injected from one side is handled by S11 while light from the other side
/// is handled by the independent S22 coefficient.
/// </summary>
public class AsymmetricS11ReflectionTests
{
    private const double Tolerance = 1e-9;

    /// <summary>
    /// Builds a 2-port S-matrix with fully independent S11/S21/S12/S22 values.
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
            { (leftPin.IDInFlow,  leftPin.IDOutFlow),  s11 },
            { (leftPin.IDInFlow,  rightPin.IDOutFlow), s21 },
            { (rightPin.IDInFlow, leftPin.IDOutFlow),  s12 },
            { (rightPin.IDInFlow, rightPin.IDOutFlow), s22 },
        });

        return (sMatrix, leftPin, rightPin);
    }

    private static MathNetVector InputAt(SMatrix sMatrix, Guid pinId)
    {
        var vec = MathNet.Numerics.LinearAlgebra.Vector<Complex>.Build.Dense(sMatrix.PinReference.Count);
        vec[sMatrix.PinReference[pinId]] = Complex.One;
        return vec;
    }

    /// <summary>
    /// Verifies that a device with S11 = 0.8 and S22 = 0.2 reports each reflection
    /// independently: injection from the left returns 0.8 reflected, injection from
    /// the right returns 0.2 reflected.
    ///
    /// If the simulator aliased S11 and S22 (e.g., using only one value for both
    /// directions), this test would fail.
    /// </summary>
    [Fact]
    public async Task AsymmetricReflector_EachSideReturnsItsOwnCoefficient()
    {
        // S11=0.8 (strong reflection from left), S22=0.2 (weak from right)
        // S21=S12=0.6 (forward/backward transmission, not power-normalised here)
        var s11 = new Complex(0.8, 0);
        var s22 = new Complex(0.2, 0);
        var s21 = new Complex(0.6, 0);
        var s12 = new Complex(0.6, 0);

        var (sMatrix, leftPin, rightPin) = BuildTwoPortMatrix(s11, s21, s12, s22);
        int stepCount = sMatrix.PinReference.Count * 2;

        // Inject from left
        using var cts1 = new CancellationTokenSource();
        var resultFromLeft = await sMatrix.CalcFieldAtPinsAfterStepsAsync(
            InputAt(sMatrix, leftPin.IDInFlow), stepCount, cts1);

        resultFromLeft[leftPin.IDOutFlow].Magnitude.ShouldBe(0.8, Tolerance,
            "S11 reflection (from left) must equal 0.8");
        resultFromLeft[rightPin.IDOutFlow].Magnitude.ShouldBe(0.6, Tolerance,
            "S21 transmission (from left) must equal 0.6");

        // Inject from right
        using var cts2 = new CancellationTokenSource();
        var resultFromRight = await sMatrix.CalcFieldAtPinsAfterStepsAsync(
            InputAt(sMatrix, rightPin.IDInFlow), stepCount, cts2);

        resultFromRight[rightPin.IDOutFlow].Magnitude.ShouldBe(0.2, Tolerance,
            "S22 reflection (from right) must equal 0.2");
        resultFromRight[leftPin.IDOutFlow].Magnitude.ShouldBe(0.6, Tolerance,
            "S12 backward transmission must equal 0.6");
    }

    /// <summary>
    /// Verifies a one-sided absorber model: S11 ≠ 0 (reflects from the left),
    /// S22 = 0 (no reflection from the right), S21 = S12 = 0 (no transmission).
    ///
    /// This represents a termination component that absorbs all forward light but
    /// reflects light coming from the left (e.g., a Bragg grating in reflection mode
    /// followed by an absorber).
    /// </summary>
    [Fact]
    public async Task OneSidedAbsorber_LeftReflectsRightDoesNot()
    {
        // Only S11 is non-zero: purely reflective from the left, absorbing from the right
        var (sMatrix, leftPin, rightPin) = BuildTwoPortMatrix(
            s11: new Complex(0.9, 0),
            s21: Complex.Zero,
            s12: Complex.Zero,
            s22: Complex.Zero);

        int stepCount = sMatrix.PinReference.Count * 2;

        // Inject from left: should see S11 reflection
        using var cts1 = new CancellationTokenSource();
        var resultFromLeft = await sMatrix.CalcFieldAtPinsAfterStepsAsync(
            InputAt(sMatrix, leftPin.IDInFlow), stepCount, cts1);

        resultFromLeft[leftPin.IDOutFlow].Magnitude.ShouldBe(0.9, Tolerance,
            "Left port should reflect with S11=0.9");
        resultFromLeft[rightPin.IDOutFlow].Magnitude.ShouldBe(0.0, Tolerance,
            "No transmission through one-sided absorber");

        // Inject from right: no reflection, no transmission
        using var cts2 = new CancellationTokenSource();
        var resultFromRight = await sMatrix.CalcFieldAtPinsAfterStepsAsync(
            InputAt(sMatrix, rightPin.IDInFlow), stepCount, cts2);

        resultFromRight[rightPin.IDOutFlow].Magnitude.ShouldBe(0.0, Tolerance,
            "Right port has S22=0: no reflection from right side");
        resultFromRight[leftPin.IDOutFlow].Magnitude.ShouldBe(0.0, Tolerance,
            "No backward transmission through one-sided absorber");
    }

    /// <summary>
    /// Verifies that the two independent injection tests above commute: swapping
    /// which port is illuminated gives the expected (different) results, confirming
    /// that the asymmetric S-matrix is applied per-direction.
    /// </summary>
    [Fact]
    public async Task AsymmetricReflector_ResultsDifferByDirection()
    {
        var (sMatrix, leftPin, rightPin) = BuildTwoPortMatrix(
            s11: new Complex(0.8, 0),
            s21: new Complex(0.6, 0),
            s12: new Complex(0.6, 0),
            s22: new Complex(0.2, 0));

        int stepCount = sMatrix.PinReference.Count * 2;

        using var cts1 = new CancellationTokenSource();
        var resultLeft = await sMatrix.CalcFieldAtPinsAfterStepsAsync(
            InputAt(sMatrix, leftPin.IDInFlow), stepCount, cts1);

        using var cts2 = new CancellationTokenSource();
        var resultRight = await sMatrix.CalcFieldAtPinsAfterStepsAsync(
            InputAt(sMatrix, rightPin.IDInFlow), stepCount, cts2);

        // The reflection seen from each side must be different
        double reflectionFromLeft  = resultLeft[leftPin.IDOutFlow].Magnitude;
        double reflectionFromRight = resultRight[rightPin.IDOutFlow].Magnitude;

        reflectionFromLeft.ShouldNotBe(reflectionFromRight,
            "Asymmetric device must produce different reflections from each port");

        // Specifically: S11 > S22
        reflectionFromLeft.ShouldBeGreaterThan(reflectionFromRight,
            "S11 (0.8) should exceed S22 (0.2)");
    }
}
