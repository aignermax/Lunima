using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using Shouldly;
using System.Numerics;
using Xunit;

namespace UnitTests.Simulation;

/// <summary>
/// Integration tests for ComponentGroup simulation.
/// Verifies that groups with computed S-Matrices work correctly in the full simulation pipeline.
/// </summary>
public class ComponentGroupSimulationTests
{
    [Fact]
    public void ComponentGroup_WithComputedSMatrix_SimulatesSuccessfully()
    {
        // Arrange - Create a group with two connected components
        var group = new ComponentGroup("SimGroup");
        var comp1 = TestComponentFactory.CreateSimpleTwoPortComponent();
        var comp2 = TestComponentFactory.CreateSimpleTwoPortComponent();

        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp2.PhysicalX = 20;
        comp2.PhysicalY = 0;

        group.AddChild(comp1);
        group.AddChild(comp2);

        // Connect comp1 output to comp2 input internally
        var frozenRoutedPath = new RoutedPath();
        frozenRoutedPath.Segments.Add(new StraightSegment(10, 0, 20, 0, 0));

        var frozenPath = new FrozenWaveguidePath
        {
            StartPin = comp1.PhysicalPins[1],
            EndPin = comp2.PhysicalPins[0],
            Path = frozenRoutedPath
        };
        group.AddInternalPath(frozenPath);

        // Expose external pins
        group.AddExternalPin(new GroupPin
        {
            Name = "GroupIn",
            InternalPin = comp1.PhysicalPins[0],
            RelativeX = 0,
            RelativeY = 0,
            AngleDegrees = 180
        });

        group.AddExternalPin(new GroupPin
        {
            Name = "GroupOut",
            InternalPin = comp2.PhysicalPins[1],
            RelativeX = 30,
            RelativeY = 0,
            AngleDegrees = 0
        });

        // Compute S-Matrix (this also populates group.PhysicalPins)
        group.ComputeSMatrix();

        // Create a light source component (grating coupler)
        var source = TestComponentFactory.CreateStraightWaveGuide();
        source.PhysicalX = -20;
        source.PhysicalY = 0;
        source.WidthMicrometers = 10;
        source.HeightMicrometers = 1;

        var sourceLogicalPin = source.Parts[0, 0].GetPinAt(CAP_Core.Tiles.RectSide.Right);
        var sourcePhysicalPin = new PhysicalPin
        {
            Name = "out",
            ParentComponent = source,
            OffsetXMicrometers = 10,
            OffsetYMicrometers = 0.5,
            AngleDegrees = 0,
            LogicalPin = sourceLogicalPin
        };
        source.PhysicalPins.Clear();
        source.PhysicalPins.Add(sourcePhysicalPin);

        // Set up grid and simulation
        var tileManager = new ComponentListTileManager();
        tileManager.AddComponent(source);
        tileManager.AddComponent(group);

        var connectionManager = new WaveguideConnectionManager(new WaveguideRouter());
        var routedPath = new RoutedPath();
        routedPath.Segments.Add(new StraightSegment(-10, 0.5, 0, 0.5, 0));

        // Connect to the GROUP's external pin, not the internal component's pin
        var groupInputPin = group.PhysicalPins.First(p => p.Name == "GroupIn");

        var connection = connectionManager.AddConnectionWithCachedRoute(
            sourcePhysicalPin,
            groupInputPin,
            routedPath);

        var portManager = new PhysicalExternalPortManager();
        var laserType = new LaserType(CAP_Core.ExternalPorts.LightColor.Red);
        var externalInput = new ExternalInput(
            "TestSource",
            laserType,
            0,
            new System.Numerics.Complex(1.0, 0),
            true);

        // Inject laser at the source's LEFT pin: light flows left→right through the
        // waveguide (Left.IDInFlow → Right.IDOutFlow) then into the group via the connection.
        var sourceLaserLogicalPin = source.Parts[0, 0].GetPinAt(CAP_Core.Tiles.RectSide.Left);
        portManager.AddLightSource(externalInput, sourceLaserLogicalPin.IDInFlow);

        var gridManager = GridManager.CreateForSimulation(
            tileManager, connectionManager, portManager);

        // Act - Run simulation
        var builder = new SystemMatrixBuilder(gridManager);
        var calculator = new GridLightCalculator(builder, gridManager);
        var cts = new CancellationTokenSource();

        var task = calculator.CalculateFieldPropagationAsync(cts, laserType.WaveLengthInNm);
        task.Wait();
        var fields = task.Result;

        // Assert - Simulation should complete without errors
        fields.ShouldNotBeNull();
        fields.Count.ShouldBeGreaterThan(0);

        // Verify that power propagated through the group
        // Check the group's external output pin
        var groupOutputPin = group.PhysicalPins.First(p => p.Name == "GroupOut");
        var groupOutputFlow = groupOutputPin.LogicalPin.IDOutFlow;

        fields.ShouldContainKey(groupOutputFlow);
        var outputPower = Complex.Abs(fields[groupOutputFlow]);
        outputPower.ShouldBeGreaterThan(0.0);
    }

    [Fact]
    public void ComponentGroup_WithoutExplicitCompute_AutoComputesSMatrix()
    {
        // Regression test for issue #305: SystemMatrixBuilder should auto-compute
        // S-Matrix for ComponentGroups that haven't been pre-computed (e.g. when
        // LightCalculationService or ParameterSweeper bypass SimulationService).
        var group = new ComponentGroup("NoMatrix");
        var comp = TestComponentFactory.CreateSimpleTwoPortComponent();
        group.AddChild(comp);

        group.AddExternalPin(new GroupPin
        {
            Name = "Pin",
            InternalPin = comp.PhysicalPins[0]
        });

        // Intentionally skip group.ComputeSMatrix() / EnsureSMatrixComputed()

        var tileManager = new ComponentListTileManager();
        tileManager.AddComponent(group);

        var connectionManager = new WaveguideConnectionManager(new WaveguideRouter());
        var portManager = new PhysicalExternalPortManager();

        var gridManager = GridManager.CreateForSimulation(
            tileManager, connectionManager, portManager);

        var builder = new SystemMatrixBuilder(gridManager);

        // Should NOT throw — SystemMatrixBuilder auto-computes the S-Matrix
        var systemMatrix = builder.GetSystemSMatrix(StandardWaveLengths.RedNM);
        systemMatrix.ShouldNotBeNull();

        // Group's S-Matrix should now be populated
        group.WaveLengthToSMatrixMap.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void ComponentGroup_SMatrixAutoComputed_WorksFor1550nm()
    {
        // Regression test for issue #305: ComponentGroup S-Matrix must be available
        // for wavelength 1550nm (StandardWaveLengths.RedNM).
        var group = new ComponentGroup("Group1550");
        var comp = TestComponentFactory.CreateSimpleTwoPortComponent();
        group.AddChild(comp);

        group.AddExternalPin(new GroupPin
        {
            Name = "In",
            InternalPin = comp.PhysicalPins[0]
        });

        group.AddExternalPin(new GroupPin
        {
            Name = "Out",
            InternalPin = comp.PhysicalPins[1]
        });

        // No pre-computation — rely on auto-compute in SystemMatrixBuilder
        var tileManager = new ComponentListTileManager();
        tileManager.AddComponent(group);

        var connectionManager = new WaveguideConnectionManager(new WaveguideRouter());
        var portManager = new PhysicalExternalPortManager();

        var gridManager = GridManager.CreateForSimulation(
            tileManager, connectionManager, portManager);

        var builder = new SystemMatrixBuilder(gridManager);

        // Must not throw InvalidDataException for 1550nm
        var systemMatrix = builder.GetSystemSMatrix(1550);
        systemMatrix.ShouldNotBeNull();
        group.WaveLengthToSMatrixMap.ShouldContainKey(1550);
    }

    [Fact]
    public void ComponentGroup_EnsureSMatrixComputed_EnablesSimulation()
    {
        // Arrange
        var group = new ComponentGroup("AutoCompute");
        var comp = TestComponentFactory.CreateSimpleTwoPortComponent();
        comp.PhysicalX = 0;
        comp.PhysicalY = 0;

        group.AddChild(comp);

        group.AddExternalPin(new GroupPin
        {
            Name = "A",
            InternalPin = comp.PhysicalPins[0],
            RelativeX = 0,
            RelativeY = 0
        });

        // Call EnsureSMatrixComputed (like SimulationService does)
        group.EnsureSMatrixComputed();

        var tileManager = new ComponentListTileManager();
        tileManager.AddComponent(group);

        var connectionManager = new WaveguideConnectionManager(new WaveguideRouter());
        var portManager = new PhysicalExternalPortManager();

        var gridManager = GridManager.CreateForSimulation(
            tileManager, connectionManager, portManager);

        // Act - Should not throw
        var builder = new SystemMatrixBuilder(gridManager);
        var systemMatrix = builder.GetSystemSMatrix(StandardWaveLengths.RedNM);

        // Assert
        systemMatrix.ShouldNotBeNull();
        systemMatrix.PinReference.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void ComponentGroup_NestedGroups_SimulateCorrectly()
    {
        // Arrange - Create nested groups
        var innerGroup = new ComponentGroup("Inner");
        var innerComp = TestComponentFactory.CreateSimpleTwoPortComponent();
        innerGroup.AddChild(innerComp);

        innerGroup.AddExternalPin(new GroupPin
        {
            Name = "In",
            InternalPin = innerComp.PhysicalPins[0]
        });

        innerGroup.AddExternalPin(new GroupPin
        {
            Name = "Out",
            InternalPin = innerComp.PhysicalPins[1]
        });

        innerGroup.ComputeSMatrix();

        var outerGroup = new ComponentGroup("Outer");
        outerGroup.AddChild(innerGroup);

        outerGroup.AddExternalPin(new GroupPin
        {
            Name = "Input",
            InternalPin = innerComp.PhysicalPins[0]
        });

        outerGroup.AddExternalPin(new GroupPin
        {
            Name = "Output",
            InternalPin = innerComp.PhysicalPins[1]
        });

        outerGroup.ComputeSMatrix();

        // Act - Set up simulation
        var tileManager = new ComponentListTileManager();
        tileManager.AddComponent(outerGroup);

        var connectionManager = new WaveguideConnectionManager(new WaveguideRouter());
        var portManager = new PhysicalExternalPortManager();

        var gridManager = GridManager.CreateForSimulation(
            tileManager, connectionManager, portManager);

        var builder = new SystemMatrixBuilder(gridManager);

        // Should not throw
        var systemMatrix = builder.GetSystemSMatrix(StandardWaveLengths.RedNM);

        // Assert
        systemMatrix.ShouldNotBeNull();
        outerGroup.WaveLengthToSMatrixMap.Count.ShouldBeGreaterThan(0);
    }
}
