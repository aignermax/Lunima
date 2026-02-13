using CAP_Core.Analysis;
using Shouldly;

namespace UnitTests.Analysis;

public class OutputPowerStatisticsTests
{
    [Fact]
    public void Constructor_WithSamples_CalculatesMean()
    {
        var samples = new List<double> { 1.0, 2.0, 3.0, 4.0, 5.0 };
        var stats = new OutputPowerStatistics(Guid.NewGuid(), samples);

        stats.MeanPower.ShouldBe(3.0, tolerance: 1e-10);
    }

    [Fact]
    public void Constructor_WithSamples_CalculatesStdDev()
    {
        var samples = new List<double> { 2.0, 4.0, 4.0, 4.0, 5.0, 5.0, 7.0, 9.0 };
        var stats = new OutputPowerStatistics(Guid.NewGuid(), samples);

        // Sample std dev of these values = 2.0
        stats.StdDeviation.ShouldBe(2.0, tolerance: 1e-10);
    }

    [Fact]
    public void Constructor_EmptySamples_ReturnsZeros()
    {
        var stats = new OutputPowerStatistics(Guid.NewGuid(), new List<double>());

        stats.MeanPower.ShouldBe(0);
        stats.StdDeviation.ShouldBe(0);
    }

    [Fact]
    public void Constructor_SingleSample_StdDevIsZero()
    {
        var samples = new List<double> { 42.0 };
        var stats = new OutputPowerStatistics(Guid.NewGuid(), samples);

        stats.MeanPower.ShouldBe(42.0);
        stats.StdDeviation.ShouldBe(0);
    }

    [Fact]
    public void Constructor_IdenticalSamples_StdDevIsZero()
    {
        var samples = new List<double> { 5.0, 5.0, 5.0 };
        var stats = new OutputPowerStatistics(Guid.NewGuid(), samples);

        stats.MeanPower.ShouldBe(5.0);
        stats.StdDeviation.ShouldBe(0, tolerance: 1e-10);
    }

    [Fact]
    public void Samples_ArePreserved()
    {
        var pinId = Guid.NewGuid();
        var samples = new List<double> { 1.0, 2.0, 3.0 };
        var stats = new OutputPowerStatistics(pinId, samples);

        stats.PinId.ShouldBe(pinId);
        stats.Samples.Count.ShouldBe(3);
        stats.Samples[0].ShouldBe(1.0);
    }
}
