using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Creation;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Simulation;

/// <summary>
/// Integration tests for prefab (SavedGroup) instantiation while simulation is active.
/// Reproduces issue #388: placing a prefab crashes when simulation is running because
/// GroupTemplateSerializer did not preserve S-Matrix data or logical pin IDs.
/// </summary>
public class PrefabInstantiationSimulationTests
{
    /// <summary>
    /// Regression test for issue #388.
    /// When a prefab is saved and reloaded (serialize → deserialize), child components
    /// must retain their S-Matrices and logical pin references so that simulation
    /// does not crash with InvalidDataException.
    /// </summary>
    [Fact]
    public void PrefabInstantiation_AfterSerializeDeserialize_DoesNotCrashSimulation()
    {
        // Arrange — create a group with a component that has a valid S-Matrix
        var group = new ComponentGroup("TestPrefab");
        var comp = TestComponentFactory.CreateSimpleTwoPortComponent();
        comp.PhysicalX = 0;
        comp.PhysicalY = 0;

        group.AddChild(comp);

        group.AddExternalPin(new GroupPin
        {
            Name = "In",
            InternalPin = comp.PhysicalPins[0],
            RelativeX = 0,
            RelativeY = 0,
            AngleDegrees = 180
        });

        group.AddExternalPin(new GroupPin
        {
            Name = "Out",
            InternalPin = comp.PhysicalPins[1],
            RelativeX = 10,
            RelativeY = 0,
            AngleDegrees = 0
        });

        group.ComputeSMatrix();
        group.IsPrefab = true;

        // Simulate save + load from disk (the step that previously stripped S-Matrices)
        var serialized = GroupTemplateSerializer.Serialize(group);
        var deserialized = GroupTemplateSerializer.Deserialize(serialized);

        deserialized.ShouldNotBeNull();

        // Instantiate the template (deep copy + ensure S-Matrix, as done during drag-and-drop)
        deserialized!.EnsureSMatrixComputed();

        // Set up simulation with the instantiated group
        var tileManager = new ComponentListTileManager();
        tileManager.AddComponent(deserialized);

        var connectionManager = new WaveguideConnectionManager(new WaveguideRouter());
        var portManager = new PhysicalExternalPortManager();

        var gridManager = GridManager.CreateForSimulation(
            tileManager, connectionManager, portManager);

        var builder = new SystemMatrixBuilder(gridManager);

        // Act — must not throw InvalidDataException ("Matrix was not defined for waveLength")
        var act = () => builder.GetSystemSMatrix(StandardWaveLengths.RedNM);

        // Assert
        act.ShouldNotThrow();
        deserialized.WaveLengthToSMatrixMap.Count.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// After deserialization, child components must have their S-Matrices restored.
    /// </summary>
    [Fact]
    public void PrefabDeserialization_ChildComponentsHaveSMatrices()
    {
        // Arrange
        var group = new ComponentGroup("MatrixCheckGroup");
        var comp = TestComponentFactory.CreateSimpleTwoPortComponent();
        group.AddChild(comp);

        group.AddExternalPin(new GroupPin
        {
            Name = "In",
            InternalPin = comp.PhysicalPins[0]
        });

        group.ComputeSMatrix();

        // Act — serialize and deserialize
        var serialized = GroupTemplateSerializer.Serialize(group);
        var deserialized = GroupTemplateSerializer.Deserialize(serialized);

        // Assert — deserialized children must have S-Matrices
        deserialized.ShouldNotBeNull();
        deserialized!.ChildComponents.Count.ShouldBe(1);

        var childAfterDeserialize = deserialized.ChildComponents[0];
        childAfterDeserialize.WaveLengthToSMatrixMap.Count.ShouldBeGreaterThan(0,
            "Child component S-Matrices must survive serialization round-trip");
    }

    /// <summary>
    /// After deserialization, physical pins must have valid LogicalPin references
    /// so that frozen internal paths and simulation connections resolve correctly.
    /// </summary>
    [Fact]
    public void PrefabDeserialization_PhysicalPinsHaveLogicalPinReferences()
    {
        // Arrange
        var group = new ComponentGroup("PinRefGroup");
        var comp = TestComponentFactory.CreateSimpleTwoPortComponent();
        group.AddChild(comp);

        group.AddExternalPin(new GroupPin
        {
            Name = "In",
            InternalPin = comp.PhysicalPins[0]
        });

        group.ComputeSMatrix();

        // Act
        var serialized = GroupTemplateSerializer.Serialize(group);
        var deserialized = GroupTemplateSerializer.Deserialize(serialized);

        // Assert — physical pins must have LogicalPin references with non-empty Guids
        deserialized.ShouldNotBeNull();
        var child = deserialized!.ChildComponents[0];

        foreach (var physicalPin in child.PhysicalPins)
        {
            physicalPin.LogicalPin.ShouldNotBeNull(
                $"Physical pin '{physicalPin.Name}' must have a LogicalPin after deserialization");
            physicalPin.LogicalPin!.IDInFlow.ShouldNotBe(Guid.Empty);
            physicalPin.LogicalPin.IDOutFlow.ShouldNotBe(Guid.Empty);
        }
    }

    /// <summary>
    /// Full prefab lifecycle: create group → save to library → load from library → place while
    /// simulation is active → simulation must complete without errors.
    /// Reproduces issue #388 end-to-end.
    /// </summary>
    [Fact]
    public void PrefabLifecycle_SaveLoadInstantiate_SimulatesWithoutCrash()
    {
        // Arrange — two-component group with internal path
        var group = new ComponentGroup("LifecyclePrefab");
        var comp1 = TestComponentFactory.CreateSimpleTwoPortComponent();
        var comp2 = TestComponentFactory.CreateSimpleTwoPortComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp2.PhysicalX = 20;
        comp2.PhysicalY = 0;

        group.AddChild(comp1);
        group.AddChild(comp2);

        var frozenPath = new FrozenWaveguidePath
        {
            StartPin = comp1.PhysicalPins[1],
            EndPin = comp2.PhysicalPins[0],
            Path = new RoutedPath()
        };
        frozenPath.Path.Segments.Add(new StraightSegment(10, 0, 20, 0, 0));
        group.AddInternalPath(frozenPath);

        group.AddExternalPin(new GroupPin
        {
            Name = "In",
            InternalPin = comp1.PhysicalPins[0],
            RelativeX = 0,
            RelativeY = 0,
            AngleDegrees = 180
        });

        group.AddExternalPin(new GroupPin
        {
            Name = "Out",
            InternalPin = comp2.PhysicalPins[1],
            RelativeX = 30,
            RelativeY = 0,
            AngleDegrees = 0
        });

        group.ComputeSMatrix();
        group.IsPrefab = true;

        // Save as template using GroupLibraryManager
        var tempDir = Path.Combine(Path.GetTempPath(), $"lunima_test_{Guid.NewGuid():N}");
        var libraryManager = new GroupLibraryManager(tempDir);
        var template = libraryManager.SaveTemplate(group, "LifecyclePrefab");

        // Simulate app restart: reload templates from disk, then instantiate
        var freshLibrary = new GroupLibraryManager(tempDir);
        freshLibrary.LoadTemplates();

        var loadedTemplate = freshLibrary.Templates.FirstOrDefault(t => t.Name == "LifecyclePrefab");
        loadedTemplate.ShouldNotBeNull("Template must be found after reload");

        // InstantiateTemplate: deep copy + EnsureSMatrixComputed (the fix must work here)
        var instance = freshLibrary.InstantiateTemplate(loadedTemplate!, 50, 0);

        // Set up simulation with the instantiated prefab
        var tileManager = new ComponentListTileManager();
        tileManager.AddComponent(instance);

        var connectionManager = new WaveguideConnectionManager(new WaveguideRouter());
        var portManager = new PhysicalExternalPortManager();

        var gridManager = GridManager.CreateForSimulation(
            tileManager, connectionManager, portManager);

        var builder = new SystemMatrixBuilder(gridManager);

        // Act — simulate: must not crash
        var act = () => builder.GetSystemSMatrix(StandardWaveLengths.RedNM);

        // Assert
        act.ShouldNotThrow();
        instance.WaveLengthToSMatrixMap.Count.ShouldBeGreaterThan(0);

        // Cleanup
        try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
    }
}
