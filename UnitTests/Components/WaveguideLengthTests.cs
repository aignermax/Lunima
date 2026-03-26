using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Components;

/// <summary>
/// Unit tests for parameterized waveguide length feature.
/// Tests target length configuration, tolerance checking, and length matching status.
/// </summary>
public class WaveguideLengthTests
{
    [Fact]
    public void TargetLength_DefaultsToNull()
    {
        var connection = CreateTestConnection();

        connection.TargetLengthMicrometers.ShouldBeNull();
        connection.IsTargetLengthEnabled.ShouldBeFalse();
    }

    [Fact]
    public void TargetLength_CanBeSet()
    {
        var connection = CreateTestConnection();

        connection.TargetLengthMicrometers = 250.0;
        connection.IsTargetLengthEnabled = true;

        connection.TargetLengthMicrometers.ShouldBe(250.0);
        connection.IsTargetLengthEnabled.ShouldBeTrue();
    }

    [Fact]
    public void LengthDifference_ReturnsNullWhenDisabled()
    {
        var connection = CreateTestConnection();
        connection.TargetLengthMicrometers = 250.0;
        connection.IsTargetLengthEnabled = false;

        connection.LengthDifference.ShouldBeNull();
    }

    [Fact]
    public void LengthDifference_ReturnsNullWhenNoTarget()
    {
        var connection = CreateTestConnection();
        connection.IsTargetLengthEnabled = true;
        connection.TargetLengthMicrometers = null;

        connection.LengthDifference.ShouldBeNull();
    }

    [Fact]
    public void LengthDifference_CalculatesCorrectly()
    {
        var connection = CreateTestConnection();
        connection.RecalculateTransmission(new WaveguideRouter());

        var actualLength = connection.PathLengthMicrometers;
        var targetLength = actualLength + 50.0;

        connection.TargetLengthMicrometers = targetLength;
        connection.IsTargetLengthEnabled = true;

        var diff = connection.LengthDifference;
        diff.ShouldNotBeNull();
        diff.Value.ShouldBe(actualLength - targetLength, 0.01);
    }

    [Fact]
    public void IsLengthMatched_ReturnsNullWhenDisabled()
    {
        var connection = CreateTestConnection();
        connection.TargetLengthMicrometers = 250.0;
        connection.IsTargetLengthEnabled = false;

        connection.IsLengthMatched.ShouldBeNull();
    }

    [Fact]
    public void IsLengthMatched_ReturnsTrueWhenWithinTolerance()
    {
        var connection = CreateTestConnection();
        connection.RecalculateTransmission(new WaveguideRouter());

        var actualLength = connection.PathLengthMicrometers;

        connection.TargetLengthMicrometers = actualLength + 0.5; // Within default 1µm tolerance
        connection.IsTargetLengthEnabled = true;
        connection.LengthToleranceMicrometers = 1.0;

        connection.IsLengthMatched.ShouldBe(true);
    }

    [Fact]
    public void IsLengthMatched_ReturnsFalseWhenOutsideTolerance()
    {
        var connection = CreateTestConnection();
        connection.RecalculateTransmission(new WaveguideRouter());

        var actualLength = connection.PathLengthMicrometers;

        connection.TargetLengthMicrometers = actualLength + 2.0; // Outside 1µm tolerance
        connection.IsTargetLengthEnabled = true;
        connection.LengthToleranceMicrometers = 1.0;

        connection.IsLengthMatched.ShouldBe(false);
    }

    [Fact]
    public void IsLengthMatched_RespectsCustomTolerance()
    {
        var connection = CreateTestConnection();
        connection.RecalculateTransmission(new WaveguideRouter());

        var actualLength = connection.PathLengthMicrometers;

        connection.TargetLengthMicrometers = actualLength + 4.5;
        connection.IsTargetLengthEnabled = true;
        connection.LengthToleranceMicrometers = 5.0; // Larger tolerance

        connection.IsLengthMatched.ShouldBe(true);
    }

    [Fact]
    public void LengthDifference_PositiveWhenActualIsLonger()
    {
        var connection = CreateTestConnection();
        connection.RecalculateTransmission(new WaveguideRouter());

        var actualLength = connection.PathLengthMicrometers;
        var targetLength = actualLength - 10.0;

        connection.TargetLengthMicrometers = targetLength;
        connection.IsTargetLengthEnabled = true;

        var diff = connection.LengthDifference;
        diff.ShouldNotBeNull();
        diff.Value.ShouldBeGreaterThan(0);
        diff.Value.ShouldBe(10.0, 0.01);
    }

    [Fact]
    public void LengthDifference_NegativeWhenActualIsShorter()
    {
        var connection = CreateTestConnection();
        connection.RecalculateTransmission(new WaveguideRouter());

        var actualLength = connection.PathLengthMicrometers;
        var targetLength = actualLength + 15.0;

        connection.TargetLengthMicrometers = targetLength;
        connection.IsTargetLengthEnabled = true;

        var diff = connection.LengthDifference;
        diff.ShouldNotBeNull();
        diff.Value.ShouldBeLessThan(0);
        diff.Value.ShouldBe(-15.0, 0.01);
    }

    /// <summary>
    /// Creates a test connection between two components.
    /// </summary>
    private WaveguideConnection CreateTestConnection()
    {
        var comp1 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;

        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp2.PhysicalX = 300;
        comp2.PhysicalY = 0;

        var connection = TestComponentFactory.CreateConnection(comp1, comp2);
        return connection;
    }
}
