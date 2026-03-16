using CAP_Core.Analysis;
using Shouldly;
using System.Numerics;
using Xunit;

namespace UnitTests.Analysis;

public class SweepResultTests
{
    [Fact]
    public void GetParameterValues_ReturnsValuesInOrder()
    {
        // Arrange
        var result = CreateSampleResult(new[] { 0.0, 0.5, 1.0 });

        // Act
        var values = result.GetParameterValues();

        // Assert
        values.Length.ShouldBe(3);
        values[0].ShouldBe(0.0);
        values[1].ShouldBe(0.5);
        values[2].ShouldBe(1.0);
    }

    [Fact]
    public void GetPowerSeriesForPin_ReturnsCorrectSeries()
    {
        // Arrange
        var pinId = Guid.NewGuid();
        var dataPoints = new List<SweepDataPoint>
        {
            new(0.0, new Dictionary<Guid, Complex> { { pinId, new Complex(0.5, 0) } }),
            new(0.5, new Dictionary<Guid, Complex> { { pinId, new Complex(0.7, 0) } }),
            new(1.0, new Dictionary<Guid, Complex> { { pinId, new Complex(1.0, 0) } }),
        };

        var config = CreateTestConfig();
        var result = new SweepResult(config, dataPoints, new List<Guid> { pinId });

        // Act
        var series = result.GetPowerSeriesForPin(pinId);

        // Assert
        series.Length.ShouldBe(3);
        series[0].ShouldBe(0.25, 1e-10);
        series[1].ShouldBe(0.49, 1e-10);
        series[2].ShouldBe(1.0, 1e-10);
    }

    [Fact]
    public void GetPowerSeriesForPin_UnknownPin_ReturnsZeros()
    {
        // Arrange
        var result = CreateSampleResult(new[] { 0.0, 0.5 });
        var unknownPin = Guid.NewGuid();

        // Act
        var series = result.GetPowerSeriesForPin(unknownPin);

        // Assert
        series.Length.ShouldBe(2);
        series[0].ShouldBe(0.0);
        series[1].ShouldBe(0.0);
    }

    [Fact]
    public void Constructor_NullConfiguration_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            new SweepResult(null!, new List<SweepDataPoint>(), new List<Guid>()));
    }

    [Fact]
    public void Constructor_NullDataPoints_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            new SweepResult(CreateTestConfig(), null!, new List<Guid>()));
    }

    [Fact]
    public void Constructor_NullPinIds_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            new SweepResult(CreateTestConfig(), new List<SweepDataPoint>(), null!));
    }

    private static SweepResult CreateSampleResult(double[] paramValues)
    {
        var pinId = Guid.NewGuid();
        var dataPoints = paramValues.Select(v =>
            new SweepDataPoint(v, new Dictionary<Guid, Complex>
            {
                { pinId, new Complex(v, 0) }
            })).ToList();

        return new SweepResult(CreateTestConfig(), dataPoints, new List<Guid> { pinId });
    }

    private static SweepConfiguration CreateTestConfig()
    {
        var component = TestComponentHelper.CreateComponentWithSlider();
        var param = new SweepParameter(component, 0, "Test");
        return new SweepConfiguration(param, 0, 1, 3, 635);
    }
}
