using CAP_Core.Analysis;
using Shouldly;
using System.Numerics;

namespace UnitTests.Analysis;

public class SweepDataPointTests
{
    [Fact]
    public void Constructor_ValidInputs_SetsParameterValue()
    {
        // Arrange
        var fields = CreateSampleFields();

        // Act
        var point = new SweepDataPoint(0.5, fields);

        // Assert
        point.ParameterValue.ShouldBe(0.5);
    }

    [Fact]
    public void Constructor_ValidInputs_ConvertsToPowers()
    {
        // Arrange
        var pinId = Guid.NewGuid();
        var fields = new Dictionary<Guid, Complex>
        {
            { pinId, new Complex(3, 4) } // magnitude = 5, power = 25
        };

        // Act
        var point = new SweepDataPoint(1.0, fields);

        // Assert
        point.OutputPowers[pinId].ShouldBe(25.0, 1e-10);
    }

    [Fact]
    public void Constructor_ZeroField_ProducesZeroPower()
    {
        // Arrange
        var pinId = Guid.NewGuid();
        var fields = new Dictionary<Guid, Complex>
        {
            { pinId, Complex.Zero }
        };

        // Act
        var point = new SweepDataPoint(0.0, fields);

        // Assert
        point.OutputPowers[pinId].ShouldBe(0.0);
    }

    [Fact]
    public void Constructor_NullFields_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            new SweepDataPoint(0.5, null!));
    }

    [Fact]
    public void Constructor_UnitMagnitudeField_ProducesUnitPower()
    {
        // Arrange
        var pinId = Guid.NewGuid();
        var fields = new Dictionary<Guid, Complex>
        {
            { pinId, Complex.FromPolarCoordinates(1.0, Math.PI / 4) }
        };

        // Act
        var point = new SweepDataPoint(0.5, fields);

        // Assert
        point.OutputPowers[pinId].ShouldBe(1.0, 1e-10);
    }

    private static Dictionary<Guid, Complex> CreateSampleFields()
    {
        return new Dictionary<Guid, Complex>
        {
            { Guid.NewGuid(), new Complex(0.5, 0.5) },
            { Guid.NewGuid(), new Complex(0.3, 0.0) }
        };
    }
}
