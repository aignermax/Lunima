using CAP_Core.Analysis.MeasuredSpectrum;
using Spectrum = CAP_Core.Analysis.MeasuredSpectrum.MeasuredSpectrum;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis.MeasuredSpectrum;

public class FringeAnalyzerTests
{
    private const double GroupIndex = 4.2;
    private const int PointCount = 600;

    /// <summary>Builds a synthetic MZI spectrum T(λ) = cos²(π·n_g·ΔL/λ), 1500–1600 nm.</summary>
    private static Spectrum SyntheticMzi(double armImbalanceUm, double noiseAmplitude = 0.0, int seed = 42)
    {
        var wl = new double[PointCount];
        var power = new double[PointCount];
        var rng = new Random(seed);
        for (int i = 0; i < PointCount; i++)
        {
            wl[i] = 1500.0 + 100.0 * i / (PointCount - 1);
            double phase = Math.PI * GroupIndex * armImbalanceUm / (wl[i] / 1000.0);
            power[i] = Math.Pow(Math.Cos(phase), 2);
            if (noiseAmplitude > 0)
                power[i] += noiseAmplitude * (rng.NextDouble() * 2.0 - 1.0);
        }
        return new Spectrum(wl, power, "synthetic");
    }

    [Theory]
    [InlineData(50.0)]
    [InlineData(100.0)]
    [InlineData(200.0)]
    public void Analyze_NoiseFreeMzi_ExtractsGroupIndexWithinOnePercent(double armImbalanceUm)
    {
        var spectrum = SyntheticMzi(armImbalanceUm);
        var result = FringeAnalyzer.Analyze(spectrum, armImbalanceUm);

        result.HasFringes.ShouldBeTrue();
        result.GroupIndex.ShouldNotBeNull();
        result.GroupIndex.Value.ShouldBe(GroupIndex, GroupIndex * 0.01);
    }

    [Fact]
    public void Analyze_NoisyMzi_ExtractsGroupIndexWithinThreePercent()
    {
        const double armImbalanceUm = 100.0;
        var spectrum = SyntheticMzi(armImbalanceUm, noiseAmplitude: 0.05);
        var result = FringeAnalyzer.Analyze(spectrum, armImbalanceUm);

        result.HasFringes.ShouldBeTrue();
        result.GroupIndex.ShouldNotBeNull();
        result.GroupIndex.Value.ShouldBe(GroupIndex, GroupIndex * 0.03);
    }

    [Fact]
    public void Analyze_Mzi_FsrMatchesTheory()
    {
        const double armImbalanceUm = 100.0;
        var spectrum = SyntheticMzi(armImbalanceUm);
        var result = FringeAnalyzer.Analyze(spectrum, armImbalanceUm);

        double lambdaUm = result.CenterWavelengthNm / 1000.0;
        double expectedFsrNm = lambdaUm * lambdaUm / (GroupIndex * armImbalanceUm) * 1000.0;
        result.MeanFsrNm.ShouldBe(expectedFsrNm, expectedFsrNm * 0.02);
        result.FsrStdDevNm.ShouldBeGreaterThanOrEqualTo(0.0);
    }

    [Fact]
    public void Analyze_FlatSpectrum_ReportsNoFringes()
    {
        var wl = Enumerable.Range(0, 200).Select(i => 1500.0 + i * 0.5).ToArray();
        var power = Enumerable.Repeat(0.5, 200).Select(p => (double)p).ToArray();
        var result = FringeAnalyzer.Analyze(new Spectrum(wl, power));

        result.HasFringes.ShouldBeFalse();
        result.GroupIndex.ShouldBeNull();
    }

    [Fact]
    public void Analyze_WithoutArmImbalance_LeavesGroupIndexNull()
    {
        var result = FringeAnalyzer.Analyze(SyntheticMzi(100.0));
        result.HasFringes.ShouldBeTrue();
        result.GroupIndex.ShouldBeNull();
    }

    [Fact]
    public void AnalyzeSweep_SweepResultAdapter_ExtractsGroupIndex()
    {
        var (sweep, pinId) = BuildSyntheticSweep(armImbalanceUm: 100.0);
        var result = FringeAnalyzer.AnalyzeSweep(sweep, pinId, armImbalanceUm: 100.0);

        result.HasFringes.ShouldBeTrue();
        result.GroupIndex.ShouldNotBeNull();
        result.GroupIndex.Value.ShouldBe(GroupIndex, GroupIndex * 0.03);
    }

    private static (CAP_Core.Analysis.OnaAnalysis.WavelengthSweepResult Sweep, Guid PinId) BuildSyntheticSweep(double armImbalanceUm)
    {
        var pinId = Guid.NewGuid();
        const int steps = 101;
        var dataPoints = new List<CAP_Core.Analysis.OnaAnalysis.WavelengthDataPoint>();
        for (int i = 0; i < steps; i++)
        {
            double wlNm = 1500.0 + i;
            double phase = Math.PI * GroupIndex * armImbalanceUm / (wlNm / 1000.0);
            double transmission = Math.Pow(Math.Cos(phase), 2);
            double amplitude = Math.Sqrt(Math.Max(transmission, 1e-12));
            var fields = new Dictionary<Guid, System.Numerics.Complex> { [pinId] = new(amplitude, 0) };
            dataPoints.Add(new CAP_Core.Analysis.OnaAnalysis.WavelengthDataPoint((int)wlNm, fields, 1.0));
        }
        var config = new CAP_Core.Analysis.OnaAnalysis.WavelengthSweepConfiguration(1500, 1600, steps);
        var sweep = new CAP_Core.Analysis.OnaAnalysis.WavelengthSweepResult(
            config, dataPoints, new List<Guid> { pinId });
        return (sweep, pinId);
    }
}
