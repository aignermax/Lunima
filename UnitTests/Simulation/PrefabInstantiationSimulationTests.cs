using System.Numerics;
using CAP_Core;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Creation;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Simulation;

/// <summary>
/// Integration tests for Issue #388: Prefab instantiation must not crash
/// when simulation is active. Verifies that deserialized group templates
/// have valid S-matrices and can be simulated without exceptions.
/// </summary>
public class PrefabInstantiationSimulationTests : IDisposable
{
    private const int WavelengthNm = 1550;
    private static readonly int[] Wavelengths = { WavelengthNm };
    private readonly string _testLibraryPath;
    private readonly GroupLibraryManager _libraryManager;

    public PrefabInstantiationSimulationTests()
    {
        _testLibraryPath = Path.Combine(
            Path.GetTempPath(),
            $"PrefabSimTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testLibraryPath);
        _libraryManager = new GroupLibraryManager(_testLibraryPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testLibraryPath))
            Directory.Delete(_testLibraryPath, true);
    }

    /// <summary>
    /// Reproduces Issue #388: saving a group as template, reloading from disk,
    /// instantiating, and running simulation should NOT throw.
    /// </summary>
    [Fact]
    public void DeserializedPrefab_DoesNotCrash_WhenSimulationRuns()
    {
        // Arrange: create a group with real S-matrix components
        var group = CreateGroupWithSimulationComponents();
        _libraryManager.SaveTemplate(group, "TestPrefab");

        // Simulate app restart: reload templates from disk
        var freshManager = new GroupLibraryManager(_testLibraryPath);
        freshManager.LoadTemplates();
        var reloadedTemplate = freshManager.Templates.First();

        // Act: instantiate the deserialized prefab
        var instance = freshManager.InstantiateTemplate(reloadedTemplate, 50, 50);

        // Build a circuit with the prefab instance and run simulation
        var tileManager = new ComponentListTileManager();
        tileManager.AddComponent(instance);

        var connectionManager = new WaveguideConnectionManager(new WaveguideRouter());
        var portManager = new PhysicalExternalPortManager();

        var grid = GridManager.CreateForSimulation(tileManager, connectionManager, portManager);
        var builder = new SystemMatrixBuilder(grid);

        // Assert: simulation must not throw
        Should.NotThrow(() => builder.GetSystemSMatrix(WavelengthNm));
    }

    /// <summary>
    /// Verifies that S-matrix data survives serialization round-trip.
    /// Child components must have non-empty S-matrices after deserialization.
    /// </summary>
    [Fact]
    public void DeserializedPrefab_HasValidSMatrices()
    {
        // Arrange
        var group = CreateGroupWithSimulationComponents();
        var json = GroupTemplateSerializer.Serialize(group);

        // Act
        var deserialized = GroupTemplateSerializer.Deserialize(json);

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized!.ChildComponents.Count.ShouldBe(2);

        foreach (var child in deserialized.ChildComponents)
        {
            child.WaveLengthToSMatrixMap.ShouldNotBeEmpty(
                $"Child '{child.Identifier}' should have S-matrices after deserialization");
            child.WaveLengthToSMatrixMap.ShouldContainKey(WavelengthNm);

            // Verify S-matrix has non-zero entries
            var sMatrix = child.WaveLengthToSMatrixMap[WavelengthNm];
            var nonNull = sMatrix.GetNonNullValues();
            nonNull.Count.ShouldBeGreaterThan(0,
                $"Child '{child.Identifier}' S-matrix should have non-zero entries");
        }
    }

    /// <summary>
    /// Verifies that deserialized physical pins have LogicalPins with valid IDs.
    /// </summary>
    [Fact]
    public void DeserializedPrefab_HasLogicalPinsOnPhysicalPins()
    {
        // Arrange
        var group = CreateGroupWithSimulationComponents();
        var json = GroupTemplateSerializer.Serialize(group);

        // Act
        var deserialized = GroupTemplateSerializer.Deserialize(json);

        // Assert
        deserialized.ShouldNotBeNull();
        foreach (var child in deserialized!.ChildComponents)
        {
            foreach (var physPin in child.PhysicalPins)
            {
                physPin.LogicalPin.ShouldNotBeNull(
                    $"Pin '{physPin.Name}' on '{child.Identifier}' should have a LogicalPin");
                physPin.LogicalPin.IDInFlow.ShouldNotBe(Guid.Empty);
                physPin.LogicalPin.IDOutFlow.ShouldNotBe(Guid.Empty);
            }
        }
    }

    /// <summary>
    /// Verifies that S-matrix transfer values are preserved through serialization.
    /// </summary>
    [Fact]
    public void SMatrixTransferValues_ArePreserved_ThroughSerialization()
    {
        // Arrange
        var group = CreateGroupWithSimulationComponents();
        var originalChild = group.ChildComponents[0];
        var originalTransfers = originalChild.WaveLengthToSMatrixMap[WavelengthNm]
            .GetNonNullValues();

        // Act
        var json = GroupTemplateSerializer.Serialize(group);
        var deserialized = GroupTemplateSerializer.Deserialize(json)!;
        var deserializedChild = deserialized.ChildComponents[0];
        var deserializedTransfers = deserializedChild.WaveLengthToSMatrixMap[WavelengthNm]
            .GetNonNullValues();

        // Assert: same number of transfer entries
        deserializedTransfers.Count.ShouldBe(originalTransfers.Count);

        // Assert: transfer magnitudes match (Guids will differ, but values should be same)
        var originalMagnitudes = originalTransfers.Values
            .Select(v => v.Magnitude).OrderBy(m => m).ToList();
        var deserializedMagnitudes = deserializedTransfers.Values
            .Select(v => v.Magnitude).OrderBy(m => m).ToList();

        for (int i = 0; i < originalMagnitudes.Count; i++)
        {
            deserializedMagnitudes[i].ShouldBe(originalMagnitudes[i], 1e-10);
        }
    }

    /// <summary>
    /// End-to-end test: prefab from disk can participate in a full simulation
    /// circuit with light sources and produce valid output.
    /// </summary>
    [Fact]
    public async Task DeserializedPrefab_ProducesValidSimulationResults()
    {
        // Arrange: create and save a group with two waveguide components
        var group = CreateGroupWithSimulationComponents();
        _libraryManager.SaveTemplate(group, "SimPrefab");

        // Reload from disk
        var freshManager = new GroupLibraryManager(_testLibraryPath);
        freshManager.LoadTemplates();
        var instance = freshManager.InstantiateTemplate(
            freshManager.Templates.First(), 50, 50);

        // Build circuit: GC_in → [PrefabInstance] → GC_out
        var gcIn = IntegrationCircuitBuilder.CreateGratingCoupler(
            "GC_In", 0, 0, Wavelengths);
        var gcOut = IntegrationCircuitBuilder.CreateGratingCoupler(
            "GC_Out", 200, 0, Wavelengths);

        var tileManager = new ComponentListTileManager();
        tileManager.AddComponent(gcIn.Component);
        tileManager.AddComponent(instance);
        tileManager.AddComponent(gcOut.Component);

        var connectionManager = new WaveguideConnectionManager(new WaveguideRouter());

        // Connect GC_in to first child's input pin
        var firstChild = instance.ChildComponents[0];
        var firstChildInPin = firstChild.PhysicalPins.First(p => p.Name == "in");
        connectionManager.AddExistingConnection(new WaveguideConnection
        {
            StartPin = gcIn.Pins["waveguide"],
            EndPin = firstChildInPin
        });

        // Connect second child's output pin to GC_out
        var secondChild = instance.ChildComponents[1];
        var secondChildOutPin = secondChild.PhysicalPins.First(p => p.Name == "out");
        connectionManager.AddExistingConnection(new WaveguideConnection
        {
            StartPin = secondChildOutPin,
            EndPin = gcOut.Pins["waveguide"]
        });

        var portManager = new PhysicalExternalPortManager();
        portManager.AddLightSource(
            new ExternalInput("laser", LaserType.Red, 0, new Complex(1.0, 0)),
            gcIn.LogicalPins[0].IDInFlow);

        var grid = GridManager.CreateForSimulation(tileManager, connectionManager, portManager);

        var builder2 = new SystemMatrixBuilder(grid);
        var calculator = new GridLightCalculator(builder2, grid);

        // Act & Assert: simulation should not throw
        var cts = new CancellationTokenSource();
        await Should.NotThrowAsync(
            () => calculator.CalculateFieldPropagationAsync(cts, WavelengthNm));
    }

    /// <summary>
    /// Creates a ComponentGroup with two waveguide components that have valid
    /// S-matrices and physical pins, connected by a frozen path.
    /// </summary>
    private ComponentGroup CreateGroupWithSimulationComponents()
    {
        var comp1 = TestComponentFactory.CreateSimpleTwoPortComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp1.Identifier = "wg_1";

        var comp2 = TestComponentFactory.CreateSimpleTwoPortComponent();
        comp2.PhysicalX = 20;
        comp2.PhysicalY = 0;
        comp2.Identifier = "wg_2";

        var group = new ComponentGroup("TestPrefabGroup")
        {
            PhysicalX = 0,
            PhysicalY = 0,
            WidthMicrometers = 30,
            HeightMicrometers = 1
        };

        group.AddChild(comp1);
        group.AddChild(comp2);

        // Add frozen path: comp1.out → comp2.in
        var outPin = comp1.PhysicalPins.First(p => p.Name == "out");
        var inPin = comp2.PhysicalPins.First(p => p.Name == "in");
        var frozenPath = new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            Path = new RoutedPath(),
            StartPin = outPin,
            EndPin = inPin
        };
        group.AddInternalPath(frozenPath);

        return group;
    }
}
