using System.Numerics;
using CAP_Core.Components;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation.PowerFlow;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Visualization;

/// <summary>
/// Unit tests for power flow analysis of frozen waveguide paths inside component groups.
/// Verifies that frozen paths are analyzed correctly alongside regular connections.
/// </summary>
public class PowerFlowFrozenPathTests
{
    /// <summary>
    /// Verifies that PowerFlowAnalyzer correctly processes frozen paths.
    /// </summary>
    [Fact]
    public void Analyze_WithFrozenPaths_CalculatesPowerFlow()
    {
        // Arrange
        var analyzer = new PowerFlowAnalyzer();

        var (frozenPath, fieldResults) = CreateTestFrozenPathWithFields();
        var frozenPaths = new List<FrozenWaveguidePath> { frozenPath };
        var connections = new List<WaveguideConnection>();

        // Act
        var result = analyzer.Analyze(connections, frozenPaths, fieldResults);

        // Assert
        result.ShouldNotBeNull();
        result.ConnectionFlows.ContainsKey(frozenPath.PathId).ShouldBeTrue();
        var flow = result.ConnectionFlows[frozenPath.PathId];
        flow.AveragePower.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// Verifies that power flow for frozen paths is normalized correctly.
    /// </summary>
    [Fact]
    public void Analyze_WithMultipleFrozenPaths_NormalizesPowerCorrectly()
    {
        // Arrange
        var analyzer = new PowerFlowAnalyzer();

        var (path1, fields1) = CreateTestFrozenPathWithFields(inputPower: 1.0);
        var (path2, fields2) = CreateTestFrozenPathWithFields(inputPower: 0.5);

        var frozenPaths = new List<FrozenWaveguidePath> { path1, path2 };
        var connections = new List<WaveguideConnection>();

        // Merge field results
        var allFields = new Dictionary<Guid, Complex>(fields1);
        foreach (var kvp in fields2)
            allFields[kvp.Key] = kvp.Value;

        // Act
        var result = analyzer.Analyze(connections, frozenPaths, allFields);

        // Assert
        var flow1 = result.ConnectionFlows[path1.PathId];
        var flow2 = result.ConnectionFlows[path2.PathId];

        flow1.NormalizedPowerFraction.ShouldBeGreaterThan(flow2.NormalizedPowerFraction);
        flow1.NormalizedPowerFraction.ShouldBe(1.0, tolerance: 0.01); // Maximum power
        flow2.NormalizedPowerFraction.ShouldBeLessThan(1.0);
    }

    /// <summary>
    /// Verifies that frozen paths and regular connections are analyzed together.
    /// </summary>
    [Fact]
    public void Analyze_WithMixedConnectionsAndFrozenPaths_AnalyzesBoth()
    {
        // Arrange
        var analyzer = new PowerFlowAnalyzer();

        var (frozenPath, frozenFields) = CreateTestFrozenPathWithFields();
        var (connection, connFields) = CreateTestConnectionWithFields();

        var frozenPaths = new List<FrozenWaveguidePath> { frozenPath };
        var connections = new List<WaveguideConnection> { connection };

        var allFields = new Dictionary<Guid, Complex>(frozenFields);
        foreach (var kvp in connFields)
            allFields[kvp.Key] = kvp.Value;

        // Act
        var result = analyzer.Analyze(connections, frozenPaths, allFields);

        // Assert
        result.ConnectionFlows.Count.ShouldBe(2);
        result.ConnectionFlows.ContainsKey(frozenPath.PathId).ShouldBeTrue();
        result.ConnectionFlows.ContainsKey(connection.Id).ShouldBeTrue();
    }

    /// <summary>
    /// Verifies that fade threshold works correctly for frozen paths.
    /// </summary>
    [Fact]
    public void Analyze_WithFadeThreshold_CorrectlyIdentifiesFadedPaths()
    {
        // Arrange
        var analyzer = new PowerFlowAnalyzer { FadeThresholdDb = -20.0 };

        var (highPowerPath, highFields) = CreateTestFrozenPathWithFields(inputPower: 1.0);
        var (lowPowerPath, lowFields) = CreateTestFrozenPathWithFields(inputPower: 0.001);

        var frozenPaths = new List<FrozenWaveguidePath> { highPowerPath, lowPowerPath };
        var connections = new List<WaveguideConnection>();

        var allFields = new Dictionary<Guid, Complex>(highFields);
        foreach (var kvp in lowFields)
            allFields[kvp.Key] = kvp.Value;

        // Act
        var result = analyzer.Analyze(connections, frozenPaths, allFields);

        // Assert
        result.IsFadedOut(highPowerPath.PathId).ShouldBeFalse();
        result.IsFadedOut(lowPowerPath.PathId).ShouldBeTrue();
    }

    /// <summary>
    /// Helper method to create a test frozen path with simulated field results.
    /// </summary>
    private static (FrozenWaveguidePath path, Dictionary<Guid, Complex> fields)
        CreateTestFrozenPathWithFields(double inputPower = 1.0)
    {
        // Create two physical pins with logical pins
        var startLogicalPin = new Pin("start", 0, MatterType.Light, RectSide.Left);
        var endLogicalPin = new Pin("end", 1, MatterType.Light, RectSide.Right);

        var startPhysicalPin = new PhysicalPin
        {
            Name = "start",
            LogicalPin = startLogicalPin,
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            AngleDegrees = 0
        };

        var endPhysicalPin = new PhysicalPin
        {
            Name = "end",
            LogicalPin = endLogicalPin,
            OffsetXMicrometers = 100,
            OffsetYMicrometers = 0,
            AngleDegrees = 180
        };

        // Create a simple straight path
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));

        var frozenPath = new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            Path = path,
            StartPin = startPhysicalPin,
            EndPin = endPhysicalPin
        };

        // Create field results simulating light flow
        var fields = new Dictionary<Guid, Complex>
        {
            // Light exits start pin
            [startLogicalPin.IDOutFlow] = new Complex(Math.Sqrt(inputPower), 0),
            // Light enters end pin (with some loss)
            [endLogicalPin.IDInFlow] = new Complex(Math.Sqrt(inputPower * 0.9), 0)
        };

        return (frozenPath, fields);
    }

    /// <summary>
    /// Helper method to create a test waveguide connection with simulated field results.
    /// </summary>
    private static (WaveguideConnection connection, Dictionary<Guid, Complex> fields)
        CreateTestConnectionWithFields(double inputPower = 1.0)
    {
        var startLogicalPin = new Pin("conn_start", 0, MatterType.Light, RectSide.Left);
        var endLogicalPin = new Pin("conn_end", 1, MatterType.Light, RectSide.Right);

        var startPhysicalPin = new PhysicalPin
        {
            Name = "conn_start",
            LogicalPin = startLogicalPin,
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            AngleDegrees = 0
        };

        var endPhysicalPin = new PhysicalPin
        {
            Name = "conn_end",
            LogicalPin = endLogicalPin,
            OffsetXMicrometers = 200,
            OffsetYMicrometers = 0,
            AngleDegrees = 180
        };

        var connection = new WaveguideConnection
        {
            StartPin = startPhysicalPin,
            EndPin = endPhysicalPin,
            Type = WaveguideType.Auto
        };

        var fields = new Dictionary<Guid, Complex>
        {
            [startLogicalPin.IDOutFlow] = new Complex(Math.Sqrt(inputPower), 0),
            [endLogicalPin.IDInFlow] = new Complex(Math.Sqrt(inputPower * 0.9), 0)
        };

        return (connection, fields);
    }
}
