using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using System.Numerics;
using Xunit;

namespace UnitTests.Routing;

public class WaveguideConnectionTests
{
    [Fact]
    public void RecalculateTransmission_ShortStraightPath_MinimalLoss()
    {
        // Arrange
        var startComponent = CreateTestComponent(0, 0);
        var endComponent = CreateTestComponent(100, 0);

        var startPin = new PhysicalPin
        {
            Name = "output",
            OffsetXMicrometers = 50,
            OffsetYMicrometers = 25,
            AngleDegrees = 0,
            ParentComponent = startComponent
        };

        var endPin = new PhysicalPin
        {
            Name = "input",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 25,
            AngleDegrees = 180,
            ParentComponent = endComponent
        };

        var connection = new WaveguideConnection
        {
            StartPin = startPin,
            EndPin = endPin,
            PropagationLossDbPerCm = 2.0,
            BendLossDbPer90Deg = 0.05
        };

        // Act
        connection.RecalculateTransmission();

        // Assert
        connection.RoutedPath.ShouldNotBeNull();
        connection.IsPathValid.ShouldBeTrue();
        connection.TotalLossDb.ShouldBeGreaterThan(0);
        connection.TransmissionCoefficient.Magnitude.ShouldBeLessThanOrEqualTo(1.0);
        connection.TransmissionCoefficient.Magnitude.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void RecalculateTransmission_LongerPath_HigherLoss()
    {
        // Arrange - two connections, one longer than the other
        var startComponent = CreateTestComponent(0, 0);
        var nearEndComponent = CreateTestComponent(100, 0);
        var farEndComponent = CreateTestComponent(1000, 0);

        var startPin1 = CreateOutputPin(startComponent);
        var endPin1 = CreateInputPin(nearEndComponent);

        var startPin2 = CreateOutputPin(startComponent);
        var endPin2 = CreateInputPin(farEndComponent);

        var shortConnection = new WaveguideConnection
        {
            StartPin = startPin1,
            EndPin = endPin1,
            PropagationLossDbPerCm = 2.0
        };

        var longConnection = new WaveguideConnection
        {
            StartPin = startPin2,
            EndPin = endPin2,
            PropagationLossDbPerCm = 2.0
        };

        // Act
        shortConnection.RecalculateTransmission();
        longConnection.RecalculateTransmission();

        // Assert
        longConnection.TotalLossDb.ShouldBeGreaterThan(shortConnection.TotalLossDb);
        longConnection.TransmissionCoefficient.Magnitude.ShouldBeLessThan(
            shortConnection.TransmissionCoefficient.Magnitude);
    }

    [Fact]
    public void RecalculateTransmission_WithBends_IncludesBendLoss()
    {
        // Arrange - offset path requires bends
        var startComponent = CreateTestComponent(0, 0);
        var endComponent = CreateTestComponent(100, 50);

        var startPin = new PhysicalPin
        {
            Name = "output",
            OffsetXMicrometers = 50,
            OffsetYMicrometers = 25,
            AngleDegrees = 0,
            ParentComponent = startComponent
        };

        var endPin = new PhysicalPin
        {
            Name = "input",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 25,
            AngleDegrees = 180,
            ParentComponent = endComponent
        };

        var connection = new WaveguideConnection
        {
            StartPin = startPin,
            EndPin = endPin,
            PropagationLossDbPerCm = 2.0,
            BendLossDbPer90Deg = 0.1 // 0.1 dB per 90-degree bend
        };

        // Act
        connection.RecalculateTransmission();

        // Assert
        connection.BendCount.ShouldBeGreaterThan(0);
        // Total loss should include both propagation and bend loss
        double propagationLossOnly = (connection.PathLengthMicrometers / 10000.0) * 2.0;
        connection.TotalLossDb.ShouldBeGreaterThan(propagationLossOnly);
    }

    [Fact]
    public void RecalculateTransmission_NullPins_ReturnsDefaultValues()
    {
        // Arrange
        var connection = new WaveguideConnection
        {
            StartPin = null!,
            EndPin = null!
        };

        // Act
        connection.RecalculateTransmission();

        // Assert
        connection.RoutedPath.ShouldBeNull();
        connection.TransmissionCoefficient.ShouldBe(Complex.One);
        connection.TotalLossDb.ShouldBe(0);
    }

    [Fact]
    public void TransmissionCoefficient_IsRealValued()
    {
        // For loss-only calculation (no phase), imaginary part should be zero
        var startComponent = CreateTestComponent(0, 0);
        var endComponent = CreateTestComponent(100, 0);

        var connection = new WaveguideConnection
        {
            StartPin = CreateOutputPin(startComponent),
            EndPin = CreateInputPin(endComponent)
        };

        // Act
        connection.RecalculateTransmission();

        // Assert
        connection.TransmissionCoefficient.Imaginary.ShouldBe(0);
    }

    private Component CreateTestComponent(double x, double y)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "test",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: $"TestComponent_{x}_{y}",
            rotationCounterClock: DiscreteRotation.R0
        );

        component.WidthMicrometers = 50;
        component.HeightMicrometers = 50;
        component.PhysicalX = x;
        component.PhysicalY = y;

        return component;
    }

    private PhysicalPin CreateOutputPin(Component component)
    {
        return new PhysicalPin
        {
            Name = "output",
            OffsetXMicrometers = component.WidthMicrometers,
            OffsetYMicrometers = component.HeightMicrometers / 2,
            AngleDegrees = 0,
            ParentComponent = component
        };
    }

    private PhysicalPin CreateInputPin(Component component)
    {
        return new PhysicalPin
        {
            Name = "input",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = component.HeightMicrometers / 2,
            AngleDegrees = 180,
            ParentComponent = component
        };
    }
}
