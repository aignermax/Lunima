using System.Numerics;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Simulation.Reflection;

/// <summary>
/// Tests back-reflected light through a directional coupler (DC).
/// A mirror on out1 (right-top port) sends light back via out1.IDInFlow;
/// the DC backward S32 path then carries it to in2.IDOutFlow (diagonal port).
/// Audit context (issue #536): verifies multi-port back-reflection propagation.
/// </summary>
public class DirectionalCouplerBackreflectionTests
{
    private const double Tolerance = 1e-6;

    // -----------------------------------------------------------------------
    // Component builders
    // -----------------------------------------------------------------------

    /// <summary>4-port DC S-matrix: through=√(1-C) real, cross=j√C imaginary.</summary>
    private static (SMatrix sMatrix, Pin in1, Pin in2, Pin out1, Pin out2) CreateDirectionalCoupler(
        double coupling = 0.5)
    {
        var in1  = new Pin("in1",  0, MatterType.Light, RectSide.Left);
        var in2  = new Pin("in2",  1, MatterType.Light, RectSide.Left);
        var out1 = new Pin("out1", 2, MatterType.Light, RectSide.Right);
        var out2 = new Pin("out2", 3, MatterType.Light, RectSide.Right);

        var through = new Complex(Math.Sqrt(1.0 - coupling), 0);
        var cross   = new Complex(0, Math.Sqrt(coupling));

        var allPinIds = new List<Guid>
        {
            in1.IDInFlow,  in1.IDOutFlow,
            in2.IDInFlow,  in2.IDOutFlow,
            out1.IDInFlow, out1.IDOutFlow,
            out2.IDInFlow, out2.IDOutFlow,
        };

        var sMatrix = new SMatrix(allPinIds, new());
        sMatrix.SetValues(new()
        {
            // Forward transfers: left-side input → right-side output
            { (in1.IDInFlow,  out1.IDOutFlow), through },   // S13: through
            { (in1.IDInFlow,  out2.IDOutFlow), cross   },   // S14: cross
            { (in2.IDInFlow,  out1.IDOutFlow), cross   },   // S23: cross
            { (in2.IDInFlow,  out2.IDOutFlow), through },   // S24: through

            // Backward transfers: right-side input → left-side output (reciprocal)
            { (out1.IDInFlow, in1.IDOutFlow),  through },   // S31: through
            { (out1.IDInFlow, in2.IDOutFlow),  cross   },   // S32: cross  ← key for diagonal back-coupling
            { (out2.IDInFlow, in1.IDOutFlow),  cross   },   // S41: cross
            { (out2.IDInFlow, in2.IDOutFlow),  through },   // S42: through
        });

        return (sMatrix, in1, in2, out1, out2);
    }

    /// <summary>Purely reflective 2-port: S11 = S22 = r, S21 = S12 = 0.</summary>
    private static (SMatrix sMatrix, Pin leftPin, Pin rightPin) CreateMirror(double r)
    {
        var leftPin  = new Pin("m_left",  0, MatterType.Light, RectSide.Left);
        var rightPin = new Pin("m_right", 1, MatterType.Light, RectSide.Right);

        var allPinIds = new List<Guid>
        {
            leftPin.IDInFlow,  leftPin.IDOutFlow,
            rightPin.IDInFlow, rightPin.IDOutFlow,
        };

        var sMatrix = new SMatrix(allPinIds, new());
        sMatrix.SetValues(new()
        {
            { (leftPin.IDInFlow,  leftPin.IDOutFlow),  new Complex(r, 0) },   // S11
            { (rightPin.IDInFlow, rightPin.IDOutFlow), new Complex(r, 0) },   // S22
        });

        return (sMatrix, leftPin, rightPin);
    }

    /// <summary>Bidirectional connection: outFlow1→inFlow2 and outFlow2→inFlow1.</summary>
    private static SMatrix CreateConnectionMatrix(
        Guid outFlow1, Guid inFlow2,
        Guid outFlow2, Guid inFlow1)
    {
        var pins = new List<Guid> { outFlow1, inFlow2, outFlow2, inFlow1 };
        var connMatrix = new SMatrix(pins.Distinct().ToList(), new());
        connMatrix.SetValues(new()
        {
            { (outFlow1, inFlow2), Complex.One },
            { (outFlow2, inFlow1), Complex.One },
        });
        return connMatrix;
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    /// <summary>
    /// Mirror on out1 must route reflected light to in2.IDOutFlow via DC S32.
    /// Path: in1→out1→mirror(S11)→out1.IDInFlow→(DC S32=j√C)→in2.IDOutFlow.
    /// Failure means S11 back-reflection is not propagated by the Neumann series.
    /// </summary>
    [Fact]
    public async Task BackreflectionFromOut1Mirror_ReachesIn2ViaDeBC_S32Path()
    {
        double mirrorR = 0.7;

        var (dcMatrix, in1, in2, out1, out2) = CreateDirectionalCoupler(0.5);
        var (mirrorMatrix, mirrorLeft, _) = CreateMirror(mirrorR);

        // Connect mirror to the right side of out1:
        //   out1.IDOutFlow → mirror.IDInFlow  (forward: light hits mirror from left)
        //   mirror.IDOutFlow → out1.IDInFlow  (reverse: reflected light re-enters DC)
        var conn = CreateConnectionMatrix(
            out1.IDOutFlow,       mirrorLeft.IDInFlow,
            mirrorLeft.IDOutFlow, out1.IDInFlow);

        var systemMatrix = SMatrix.CreateSystemSMatrix(
            new List<SMatrix> { dcMatrix, mirrorMatrix, conn });

        // Laser at in1
        var inputVec = MathNet.Numerics.LinearAlgebra.Vector<Complex>.Build.Dense(
            systemMatrix.PinReference.Count);
        inputVec[systemMatrix.PinReference[in1.IDInFlow]] = Complex.One;

        int stepCount = systemMatrix.PinReference.Count * 2;
        using var cts = new CancellationTokenSource();

        var result = await systemMatrix.CalcFieldAtPinsAfterStepsAsync(inputVec, stepCount, cts);

        // Forward path: out2 must have cross-coupled field (baseline check)
        result[out2.IDOutFlow].Magnitude.ShouldBeGreaterThan(0,
            "out2 must have field from forward path (DC S14 cross-coupling from in1)");

        // Back-reflection path: in2.IDOutFlow must be non-zero
        result[in2.IDOutFlow].Magnitude.ShouldBeGreaterThan(0,
            "in2.IDOutFlow must carry back-reflected field distributed by DC S32. " +
            "Propagation path: in1→out1→mirror→out1.IDInFlow→in2.IDOutFlow. " +
            "If zero, S11/back-reflection is not being propagated by the Neumann series.");

        // Also verify in1.IDOutFlow is non-zero (DC S31 through-backward path)
        result[in1.IDOutFlow].Magnitude.ShouldBeGreaterThan(0,
            "in1.IDOutFlow must carry back-reflected field via DC S31 (through backward)");
    }

    /// <summary>
    /// Without mirror: in2.IDOutFlow = 0 (no backward path).
    /// With mirror: in2.IDOutFlow > 0 (S11 drives backward path into DC).
    /// </summary>
    [Fact]
    public async Task MirrorOnOut1_ChangesIn2OutputComparedToNoMirror()
    {
        double mirrorR = 0.7;

        // --- Circuit A: coupler + mirror on out1 ---
        var (dcA, in1A, in2A, out1A, _) = CreateDirectionalCoupler(0.5);
        var (mirrorMatrix, mirrorLeft, _) = CreateMirror(mirrorR);
        var connA = CreateConnectionMatrix(
            out1A.IDOutFlow,       mirrorLeft.IDInFlow,
            mirrorLeft.IDOutFlow,  out1A.IDInFlow);

        var sysA = SMatrix.CreateSystemSMatrix(new List<SMatrix> { dcA, mirrorMatrix, connA });
        var inputA = MathNet.Numerics.LinearAlgebra.Vector<Complex>.Build.Dense(sysA.PinReference.Count);
        inputA[sysA.PinReference[in1A.IDInFlow]] = Complex.One;

        using var ctsA = new CancellationTokenSource();
        var resultA = await sysA.CalcFieldAtPinsAfterStepsAsync(
            inputA, sysA.PinReference.Count * 2, ctsA);
        double in2WithMirror = resultA[in2A.IDOutFlow].Magnitude;

        // --- Circuit B: coupler only (no mirror, open-ended out1) ---
        var (dcB, in1B, in2B, _, _) = CreateDirectionalCoupler(0.5);
        var sysB = SMatrix.CreateSystemSMatrix(new List<SMatrix> { dcB });
        var inputB = MathNet.Numerics.LinearAlgebra.Vector<Complex>.Build.Dense(sysB.PinReference.Count);
        inputB[sysB.PinReference[in1B.IDInFlow]] = Complex.One;

        using var ctsB = new CancellationTokenSource();
        var resultB = await sysB.CalcFieldAtPinsAfterStepsAsync(
            inputB, sysB.PinReference.Count * 2, ctsB);
        double in2WithoutMirror = resultB[in2B.IDOutFlow].Magnitude;

        // Without mirror: in2.IDOutFlow has no backward driving source → should be zero
        in2WithoutMirror.ShouldBe(0.0, Tolerance,
            "Without a mirror, in2.IDOutFlow should be zero (no backward path into the DC)");

        // With mirror: in2.IDOutFlow should be non-zero
        in2WithMirror.ShouldBeGreaterThan(Tolerance,
            $"With mirror (R={mirrorR}) on out1, in2.IDOutFlow must be non-zero. " +
            $"If still zero, back-reflection from S11 is not propagating through the Neumann series.");
    }

    /// <summary>
    /// Direct injection at out1.IDInFlow must distribute via S31 → in1.IDOutFlow (through)
    /// and S32 → in2.IDOutFlow (cross). Isolates backward-path physics from mirror S11.
    /// </summary>
    [Fact]
    public async Task BackwardInjectionAtOut1_DistributesToBothInputPorts()
    {
        double coupling = 0.5;
        double through = Math.Sqrt(1.0 - coupling); // √0.5 ≈ 0.7071
        double cross   = Math.Sqrt(coupling);        // √0.5 ≈ 0.7071

        var (dcMatrix, in1, in2, out1, _) = CreateDirectionalCoupler(coupling);

        // Inject directly at out1.IDInFlow (backward wave entering from the right)
        var inputVec = MathNet.Numerics.LinearAlgebra.Vector<Complex>.Build.Dense(
            dcMatrix.PinReference.Count);
        inputVec[dcMatrix.PinReference[out1.IDInFlow]] = Complex.One;

        int stepCount = dcMatrix.PinReference.Count * 2;
        using var cts = new CancellationTokenSource();

        var result = await dcMatrix.CalcFieldAtPinsAfterStepsAsync(inputVec, stepCount, cts);

        // S31: through-backward path → in1.IDOutFlow
        result[in1.IDOutFlow].Magnitude.ShouldBe(through, Tolerance,
            $"DC S31 (through backward) must give amplitude √(1−coupling) = {through:F4} at in1.IDOutFlow");

        // S32: cross-backward path → in2.IDOutFlow (the diagonally-opposite input port)
        result[in2.IDOutFlow].Magnitude.ShouldBe(cross, Tolerance,
            $"DC S32 (cross backward) must give amplitude √coupling = {cross:F4} at in2.IDOutFlow. " +
            "This is the 'diagonally-opposite port' back-coupling path from issue #536.");
    }
}
