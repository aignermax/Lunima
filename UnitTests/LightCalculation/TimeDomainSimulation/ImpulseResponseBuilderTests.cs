using CAP_Core.Components.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.TimeDomainSimulation;
using Moq;
using Shouldly;
using System.Numerics;
using Xunit;

namespace UnitTests.LightCalculation.TimeDomainSimulation;

public class ImpulseResponseBuilderTests
{
    private const double SpeedOfLightNmPerS = 2.998e17;
    private const double CenterWavelengthNm = 1550;
    private const double SpanNm = 100;
    private const int NPoints = 64;

    // Frequency grid helper (mirrors ImpulseResponseBuilder.BuildFrequencyGrid)
    private static double[] BuildFreqGrid()
    {
        double fMin = SpeedOfLightNmPerS / (CenterWavelengthNm + SpanNm / 2.0);
        double fMax = SpeedOfLightNmPerS / (CenterWavelengthNm - SpanNm / 2.0);
        double df = (fMax - fMin) / (NPoints - 1);
        return Enumerable.Range(0, NPoints).Select(i => fMin + i * df).ToArray();
    }

    private static double ComputeBandwidth()
    {
        double fMin = SpeedOfLightNmPerS / (CenterWavelengthNm + SpanNm / 2.0);
        double fMax = SpeedOfLightNmPerS / (CenterWavelengthNm - SpanNm / 2.0);
        return fMax - fMin;
    }

    private static SMatrix CreateTwoPortMatrix(Guid inputPinId, Guid outputPinId, Complex s21)
    {
        var allPins = new List<Guid> { inputPinId, outputPinId };
        var matrix = new SMatrix(allPins, new());
        matrix.SetValues(new Dictionary<(Guid, Guid), Complex>
        {
            { (inputPinId, outputPinId), s21 }
        });
        return matrix;
    }

    [Fact]
    public void Build_ConstantSMatrix_ImpulseResponsePeakAtIndexZero()
    {
        // Arrange: S_21 = 1 at all wavelengths → h[0] = 1, h[others] ≈ 0
        var inputPinId = Guid.NewGuid();
        var outputPinId = Guid.NewGuid();

        var mockBuilder = new Mock<ISystemMatrixBuilder>();
        mockBuilder.Setup(b => b.GetSystemSMatrix(It.IsAny<int>()))
            .Returns((int _) => CreateTwoPortMatrix(inputPinId, outputPinId, Complex.One));

        var builder = new ImpulseResponseBuilder(mockBuilder.Object);

        // Act
        var impulseResponses = builder.Build(CenterWavelengthNm, SpanNm, NPoints);

        // Assert: single connection with peak at index 0
        impulseResponses.Count.ShouldBe(1);
        var h = impulseResponses[0].Samples;
        int peakIdx = h.Select((v, i) => (Mag: v.Magnitude, Idx: i)).MaxBy(x => x.Mag).Idx;
        peakIdx.ShouldBe(0);
        h[0].Magnitude.ShouldBeGreaterThan(0.9); // Should be close to 1
    }

    [Fact]
    public void Build_LinearPhaseSMatrix_ImpulseResponsePeakAtGroupDelay()
    {
        // Arrange: S_21(f) = exp(-2πi * f * tau_g) → peak at round(bandwidth * tau_g)
        int expectedDelaySamples = 5;
        double bandwidth = ComputeBandwidth();
        double tauGroupDelay = expectedDelaySamples / bandwidth;

        var inputPinId = Guid.NewGuid();
        var outputPinId = Guid.NewGuid();

        var mockBuilder = new Mock<ISystemMatrixBuilder>();
        mockBuilder.Setup(b => b.GetSystemSMatrix(It.IsAny<int>()))
            .Returns((int wavelengthNm) =>
            {
                double freq = SpeedOfLightNmPerS / wavelengthNm;
                Complex s21 = Complex.Exp(new Complex(0, -2 * Math.PI * freq * tauGroupDelay));
                return CreateTwoPortMatrix(inputPinId, outputPinId, s21);
            });

        var builder = new ImpulseResponseBuilder(mockBuilder.Object);

        // Act
        var impulseResponses = builder.Build(CenterWavelengthNm, SpanNm, NPoints);

        // Assert: peak should be at expectedDelaySamples (within ±1 due to rounding)
        var h = impulseResponses[0].Samples;
        int peakIdx = h.Select((v, i) => (Mag: v.Magnitude, Idx: i)).MaxBy(x => x.Mag).Idx;
        Math.Abs(peakIdx - expectedDelaySamples).ShouldBeLessThanOrEqualTo(1,
            $"Expected peak near sample {expectedDelaySamples}, got {peakIdx}");
    }

    [Fact]
    public void Build_WithNonlinearConnections_ThrowsInvalidOperationException()
    {
        // Arrange: system matrix has a nonlinear connection
        var inputPinId = Guid.NewGuid();
        var outputPinId = Guid.NewGuid();
        var allPins = new List<Guid> { inputPinId, outputPinId };

        var mockBuilder = new Mock<ISystemMatrixBuilder>();
        mockBuilder.Setup(b => b.GetSystemSMatrix(It.IsAny<int>()))
            .Returns((int _) =>
            {
                var matrix = new SMatrix(allPins, new());
                var key = (inputPinId, outputPinId);
                var nonLinFn = new ConnectionFunction(
                    _ => Complex.One,
                    "1",
                    new List<Guid>(),
                    IsInnerLoopFunction: false);
                matrix.NonLinearConnections.Add(key, nonLinFn);
                return matrix;
            });

        var builder = new ImpulseResponseBuilder(mockBuilder.Object);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() =>
            builder.Build(CenterWavelengthNm, SpanNm, NPoints));
    }

    [Fact]
    public void Build_TooManyPoints_ThrowsInvalidOperationException()
    {
        var mockBuilder = new Mock<ISystemMatrixBuilder>();
        var builder = new ImpulseResponseBuilder(mockBuilder.Object);

        // 100,000 points would far exceed the 10 MB memory limit
        Should.Throw<InvalidOperationException>(() =>
            builder.Build(CenterWavelengthNm, SpanNm, nPoints: 100_000));
    }

    [Fact]
    public void Build_InvalidNPoints_ThrowsArgumentOutOfRangeException()
    {
        var mockBuilder = new Mock<ISystemMatrixBuilder>();
        var builder = new ImpulseResponseBuilder(mockBuilder.Object);

        Should.Throw<ArgumentOutOfRangeException>(() =>
            builder.Build(CenterWavelengthNm, SpanNm, nPoints: 1));
    }

    [Fact]
    public void Build_NullMatrixBuilder_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new ImpulseResponseBuilder(null!));
    }

    [Fact]
    public void Build_TimeStepMatchesBandwidth()
    {
        // Arrange: constant S_21 = 1
        var inputPinId = Guid.NewGuid();
        var outputPinId = Guid.NewGuid();

        var mockBuilder = new Mock<ISystemMatrixBuilder>();
        mockBuilder.Setup(b => b.GetSystemSMatrix(It.IsAny<int>()))
            .Returns((int _) => CreateTwoPortMatrix(inputPinId, outputPinId, Complex.One));

        var builder = new ImpulseResponseBuilder(mockBuilder.Object);

        // Act
        var impulseResponses = builder.Build(CenterWavelengthNm, SpanNm, NPoints);

        // Assert: dt = 1 / bandwidth
        double expectedDt = 1.0 / ComputeBandwidth();
        impulseResponses[0].TimeStepSeconds.ShouldBe(expectedDt, 1e-30);
    }
}
