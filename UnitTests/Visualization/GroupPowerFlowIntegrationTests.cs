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
    /// Regression test for issue #314: frozen paths showed gray (zero power) after simulation
    /// because fieldResults from the global simulation only contains external group pin IDs,
    /// not the internal frozen path pin IDs. The fix augments fieldResults by propagating
    /// external amplitudes inward through the group's cached InternalSystemMatrix.
    /// </summary>
    [Fact]
    public void UpdateFromSimulation_WhenFieldResultsOnlyHaveExternalGroupPins_FrozenPathsShowPower()
    {
        // Arrange: create a group with two components connected by a frozen path.
        // Both components have real S-Matrices so InternalSystemMatrix is computed.
        var (group, frozenPath, externalInputPinId) = CreateGroupWithSMatrixComponents();

        var components = new List<Component> { group };
        var connections = new List<WaveguideConnection>();

        // Simulate what the real global simulation produces:
        // fieldResults ONLY contains external group pin amplitudes — NOT internal frozen path pins.
        var fieldResults = new Dictionary<Guid, Complex>
        {
            [externalInputPinId] = new Complex(1.0, 0)
        };

        var visualizer = new PowerFlowVisualizer();

        // Act
        visualizer.UpdateFromSimulation(connections, components, fieldResults);

        // Assert: frozen path must show non-zero power (was always 0 before the fix)
        var flow = visualizer.GetFlowForConnection(frozenPath.PathId);
        flow.ShouldNotBeNull();
        flow!.AveragePower.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// Creates a group with two components that have actual S-Matrices:
    ///   Comp1: external pin A (left) → internal pin B (right, frozen path start)
    ///   Comp2: internal pin C (left, frozen path end) → external pin D (right)
    /// Returns the group, frozen path, and the IDInFlow of external pin A.
    /// </summary>
    private static (ComponentGroup group, FrozenWaveguidePath frozenPath, Guid externalInputPinId)
        CreateGroupWithSMatrixComponents()
    {
        const int Wavelength = 1550;

        var pinA = new Pin("pinA", 0, MatterType.Light, RectSide.Left);
        var pinB = new Pin("pinB", 1, MatterType.Light, RectSide.Right);
        var physPinA = new PhysicalPin { Name = "pinA", LogicalPin = pinA };
        var physPinB = new PhysicalPin { Name = "pinB", LogicalPin = pinB };
        var comp1 = CreatePassthroughComponent("comp1", pinA, pinB, physPinA, physPinB, Wavelength);

        var pinC = new Pin("pinC", 0, MatterType.Light, RectSide.Left);
        var pinD = new Pin("pinD", 1, MatterType.Light, RectSide.Right);
        var physPinC = new PhysicalPin { Name = "pinC", LogicalPin = pinC };
        var physPinD = new PhysicalPin { Name = "pinD", LogicalPin = pinD };
        var comp2 = CreatePassthroughComponent("comp2", pinC, pinD, physPinC, physPinD, Wavelength);

        var group = new ComponentGroup("BugReproGroup");
        group.AddChild(comp1);
        group.AddChild(comp2);

        var frozenPath = new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            Path = CreateSimpleStraightPath(),
            StartPin = physPinB,
            EndPin = physPinC
        };
        group.AddInternalPath(frozenPath);

        group.AddExternalPin(new GroupPin { Name = "groupA", InternalPin = physPinA });
        group.AddExternalPin(new GroupPin { Name = "groupD", InternalPin = physPinD });

        // ComputeSMatrix caches InternalSystemMatrix — required for the fix to work
        group.ComputeSMatrix();

        return (group, frozenPath, pinA.IDInFlow);
    }

    /// <summary>
    /// Creates a component whose S-Matrix transfers light from inPin's IDInFlow to outPin's IDOutFlow.
    /// </summary>
    private static Component CreatePassthroughComponent(
        string id, Pin inPin, Pin outPin,
        PhysicalPin physIn, PhysicalPin physOut,
        int wavelength)
    {
        var pinIds = new List<Guid>
        {
            inPin.IDInFlow, inPin.IDOutFlow,
            outPin.IDInFlow, outPin.IDOutFlow
        };
        var sMatrix = new SMatrix(pinIds, new());
        sMatrix.SetValues(new Dictionary<(Guid, Guid), Complex>
        {
            { (inPin.IDInFlow, outPin.IDOutFlow), Complex.One }
        });
        var comp = new Component(
            new Dictionary<int, SMatrix> { [wavelength] = sMatrix },
            new List<Slider>(), "passthrough", "",
            new Part[1, 1] { { new Part() } },
            0, id, new DiscreteRotation(),
            new List<PhysicalPin> { physIn, physOut });
        physIn.ParentComponent = comp;
        physOut.ParentComponent = comp;
        return comp;
    }

    /// <summary>
    /// Creates a simple single-segment straight path for frozen path geometry.
    /// </summary>
    private static RoutedPath CreateSimpleStraightPath()
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));
        return path;
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
