using System.Numerics;
using CAP.Avalonia.Visualization;
using CAP_Core.Components;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Visualization;

/// <summary>
/// Integration tests verifying that power flow visualization works correctly
/// for frozen paths inside component groups.
/// </summary>
public class GroupPowerFlowIntegrationTests
{
    /// <summary>
    /// Verifies that PowerFlowVisualizer collects frozen paths from groups
    /// and analyzes them correctly.
    /// </summary>
    [Fact]
    public void UpdateFromSimulation_WithComponentGroups_AnalyzesFrozenPaths()
    {
        // Arrange
        var visualizer = new PowerFlowVisualizer();

        var (group, frozenPath) = CreateTestGroupWithFrozenPath();
        var components = new List<Component> { group };
        var connections = new List<WaveguideConnection>();

        var fieldResults = CreateFieldResultsForFrozenPath(frozenPath);

        // Act
        visualizer.UpdateFromSimulation(connections, components, fieldResults);

        // Assert
        visualizer.CurrentResult.ShouldNotBeNull();
        visualizer.CurrentResult!.ConnectionFlows.ContainsKey(frozenPath.PathId).ShouldBeTrue();

        var flow = visualizer.CurrentResult.ConnectionFlows[frozenPath.PathId];
        flow.AveragePower.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// Verifies that nested groups' frozen paths are also collected and analyzed.
    /// </summary>
    [Fact]
    public void UpdateFromSimulation_WithNestedGroups_AnalyzesAllFrozenPaths()
    {
        // Arrange
        var visualizer = new PowerFlowVisualizer();

        // Create outer group with one frozen path
        var (outerGroup, outerPath) = CreateTestGroupWithFrozenPath();

        // Create inner group with one frozen path
        var (innerGroup, innerPath) = CreateTestGroupWithFrozenPath();

        // Nest the inner group inside the outer group
        outerGroup.AddChild(innerGroup);

        var components = new List<Component> { outerGroup };
        var connections = new List<WaveguideConnection>();

        var outerFields = CreateFieldResultsForFrozenPath(outerPath);
        var innerFields = CreateFieldResultsForFrozenPath(innerPath);

        var allFields = new Dictionary<Guid, Complex>(outerFields);
        foreach (var kvp in innerFields)
            allFields[kvp.Key] = kvp.Value;

        // Act
        visualizer.UpdateFromSimulation(connections, components, allFields);

        // Assert
        visualizer.CurrentResult.ShouldNotBeNull();
        visualizer.CurrentResult!.ConnectionFlows.Count.ShouldBe(2);
        visualizer.CurrentResult.ConnectionFlows.ContainsKey(outerPath.PathId).ShouldBeTrue();
        visualizer.CurrentResult.ConnectionFlows.ContainsKey(innerPath.PathId).ShouldBeTrue();
    }

    /// <summary>
    /// Verifies that both regular connections and frozen paths are visualized together.
    /// </summary>
    [Fact]
    public void UpdateFromSimulation_WithMixedElements_AnalyzesBoth()
    {
        // Arrange
        var visualizer = new PowerFlowVisualizer();

        var (group, frozenPath) = CreateTestGroupWithFrozenPath();
        var (connection, connFields) = CreateTestConnection();

        var components = new List<Component> { group };
        var connections = new List<WaveguideConnection> { connection };

        var frozenFields = CreateFieldResultsForFrozenPath(frozenPath);
        var allFields = new Dictionary<Guid, Complex>(frozenFields);
        foreach (var kvp in connFields)
            allFields[kvp.Key] = kvp.Value;

        // Act
        visualizer.UpdateFromSimulation(connections, components, allFields);

        // Assert
        visualizer.CurrentResult.ShouldNotBeNull();
        visualizer.CurrentResult!.ConnectionFlows.Count.ShouldBe(2);
        visualizer.CurrentResult.ConnectionFlows.ContainsKey(frozenPath.PathId).ShouldBeTrue();
        visualizer.CurrentResult.ConnectionFlows.ContainsKey(connection.Id).ShouldBeTrue();
    }

    /// <summary>
    /// Verifies that GetFlowForConnection works for frozen path IDs.
    /// </summary>
    [Fact]
    public void GetFlowForConnection_WithFrozenPathId_ReturnsFlow()
    {
        // Arrange
        var visualizer = new PowerFlowVisualizer();

        var (group, frozenPath) = CreateTestGroupWithFrozenPath();
        var components = new List<Component> { group };
        var connections = new List<WaveguideConnection>();

        var fieldResults = CreateFieldResultsForFrozenPath(frozenPath);

        visualizer.UpdateFromSimulation(connections, components, fieldResults);

        // Act
        var flow = visualizer.GetFlowForConnection(frozenPath.PathId);

        // Assert
        flow.ShouldNotBeNull();
        flow!.ConnectionId.ShouldBe(frozenPath.PathId);
        flow.AveragePower.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// Helper method to create a test component group with a frozen path.
    /// </summary>
    private static (ComponentGroup group, FrozenWaveguidePath frozenPath)
        CreateTestGroupWithFrozenPath()
    {
        var group = new ComponentGroup("TestGroup");

        // Create two simple components
        var comp1 = CreateSimpleComponent("comp1", 0, 0);
        var comp2 = CreateSimpleComponent("comp2", 100, 0);

        group.AddChild(comp1);
        group.AddChild(comp2);

        // Create a frozen path between them
        var startPin = comp1.PhysicalPins[0];
        var endPin = comp2.PhysicalPins[0];

        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));

        var frozenPath = new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            Path = path,
            StartPin = startPin,
            EndPin = endPin
        };

        group.AddInternalPath(frozenPath);

        return (group, frozenPath);
    }

    /// <summary>
    /// Helper method to create a simple component with one pin.
    /// </summary>
    private static Component CreateSimpleComponent(string id, double x, double y)
    {
        var logicalPin = new Pin("pin0", 0, MatterType.Light, RectSide.Right);
        var physicalPin = new PhysicalPin
        {
            Name = "pin0",
            LogicalPin = logicalPin,
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            AngleDegrees = 0
        };

        var component = new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "test",
            "",
            new Part[1, 1] { { new Part() } },
            0,
            id,
            new DiscreteRotation(),
            new List<PhysicalPin> { physicalPin })
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = 50,
            HeightMicrometers = 50
        };

        physicalPin.ParentComponent = component;
        return component;
    }

    /// <summary>
    /// Helper method to create field results for a frozen path.
    /// </summary>
    private static Dictionary<Guid, Complex> CreateFieldResultsForFrozenPath(FrozenWaveguidePath path)
    {
        var fields = new Dictionary<Guid, Complex>();

        if (path.StartPin.LogicalPin != null)
        {
            fields[path.StartPin.LogicalPin.IDOutFlow] = new Complex(1.0, 0);
        }

        if (path.EndPin.LogicalPin != null)
        {
            fields[path.EndPin.LogicalPin.IDInFlow] = new Complex(0.9, 0);
        }

        return fields;
    }

    /// <summary>
    /// Helper method to create a test waveguide connection.
    /// </summary>
    private static (WaveguideConnection connection, Dictionary<Guid, Complex> fields)
        CreateTestConnection()
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
            [startLogicalPin.IDOutFlow] = new Complex(1.0, 0),
            [endLogicalPin.IDInFlow] = new Complex(0.9, 0)
        };

        return (connection, fields);
    }
}
