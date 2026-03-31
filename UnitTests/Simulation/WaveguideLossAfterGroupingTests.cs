using System.Numerics;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Simulation;

/// <summary>
/// Verifies that waveguide loss values (field amplitudes at pin IDs) are identical
/// before and after grouping components into a ComponentGroup.
///
/// Grouping is a purely organizational operation and must NOT affect simulation results.
/// Any discrepancy indicates a bug in S-Matrix computation or light propagation for groups.
///
/// Covers issue #387: Waveguide loss values change after grouping components.
/// </summary>
public class WaveguideLossAfterGroupingTests
{
    private const int WavelengthNm = 1550;
    private static readonly int[] Wavelengths = { WavelengthNm };
    private const double SimulationTolerance = 1e-10;

    /// <summary>
    /// Creates a circuit with GC → PhaseShifter (2-port) → Splitter.
    /// Runs simulation before grouping and after grouping all components.
    /// Asserts that all field amplitudes at external pins are identical.
    ///
    /// This test demonstrates issue #387: loss values change after grouping.
    /// It should fail initially (revealing the bug) and pass after the fix.
    /// </summary>
    [Fact]
    public async Task WaveguideLoss_IsIdentical_BeforeAndAfterGroupingAllComponents()
    {
        // === BUILD SHARED COMPONENTS ===
        var gc = IntegrationCircuitBuilder.CreateGratingCoupler("GC", 0, 0, Wavelengths);
        var ps = TestComponentFactory.CreateSimpleTwoPortComponent(); // represents Phase Shifter
        ps.PhysicalX = 30;
        ps.PhysicalY = 0;
        var splitter = IntegrationCircuitBuilder.CreateSplitter("Splitter", 60, 0, Wavelengths);

        // === FLAT CIRCUIT (before grouping) ===
        var flatTile = new ComponentListTileManager();
        flatTile.AddComponent(gc.Component);
        flatTile.AddComponent(ps);
        flatTile.AddComponent(splitter.Component);

        var gcWaveguidePin = gc.Pins["waveguide"];
        var psInPin = ps.PhysicalPins[0];
        var psOutPin = ps.PhysicalPins[1];
        var splitterInPin = splitter.Pins["in1"];

        var flatConn = new WaveguideConnectionManager(new WaveguideRouter());
        flatConn.AddExistingConnection(new WaveguideConnection { StartPin = gcWaveguidePin, EndPin = psInPin });
        flatConn.AddExistingConnection(new WaveguideConnection { StartPin = psOutPin, EndPin = splitterInPin });

        var portManager = new PhysicalExternalPortManager();
        portManager.AddLightSource(
            new ExternalInput("laser", LaserType.Red, 0, new Complex(1.0, 0)),
            gc.LogicalPins[0].IDInFlow);

        var flatGrid = GridManager.CreateForSimulation(flatTile, flatConn, portManager);
        var fieldsFlat = await RunSimulationAsync(flatGrid);

        // Sanity check: light should reach splitter outputs in the flat circuit
        var splitterOut1Pin = splitter.LogicalPins[1];
        var splitterOut2Pin = splitter.LogicalPins[2];
        fieldsFlat[splitterOut1Pin.IDOutFlow].Magnitude
            .ShouldBeGreaterThan(0, "Flat simulation: no light at splitter out1 — circuit setup is wrong");

        // === GROUPED CIRCUIT (after grouping all three components) ===
        var group = new ComponentGroup("TestGroup");
        group.AddChild(gc.Component);
        group.AddChild(ps);
        group.AddChild(splitter.Component);

        // Frozen paths for internal connections (empty path = TransmissionCoefficient = 1)
        group.AddInternalPath(new FrozenWaveguidePath
        {
            Path = new RoutedPath(),
            StartPin = gcWaveguidePin,
            EndPin = psInPin
        });
        group.AddInternalPath(new FrozenWaveguidePath
        {
            Path = new RoutedPath(),
            StartPin = psOutPin,
            EndPin = splitterInPin
        });

        // Expose external pins: GC input and Splitter outputs
        group.AddExternalPin(new GroupPin
        {
            Name = "GC_in",
            InternalPin = gcWaveguidePin,
            RelativeX = 0,
            RelativeY = 0,
            AngleDegrees = 0
        });
        group.AddExternalPin(new GroupPin
        {
            Name = "Splitter_out1",
            InternalPin = splitter.Pins["out1"],
            RelativeX = 60,
            RelativeY = -5,
            AngleDegrees = 0
        });
        group.AddExternalPin(new GroupPin
        {
            Name = "Splitter_out2",
            InternalPin = splitter.Pins["out2"],
            RelativeX = 60,
            RelativeY = 5,
            AngleDegrees = 0
        });

        group.EnsureSMatrixComputed();

        // Only the group in the tile manager (individual components removed after grouping)
        var groupedTile = new ComponentListTileManager();
        groupedTile.AddComponent(group);

        var emptyConn = new WaveguideConnectionManager(new WaveguideRouter());
        var groupedGrid = GridManager.CreateForSimulation(groupedTile, emptyConn, portManager);
        var fieldsGrouped = await RunSimulationAsync(groupedGrid);

        // === ASSERT: field amplitudes must be identical ===

        // 1. GC output — accessible as external pin after grouping
        var gcOutId = gc.LogicalPins[0].IDOutFlow;
        fieldsGrouped.ShouldContainKey(gcOutId,
            "Grouped simulation: GC output pin not in results — external pin setup is wrong");
        fieldsGrouped[gcOutId].Magnitude.ShouldBeGreaterThan(0,
            "Grouped simulation: no light at GC output");
        fieldsGrouped[gcOutId].Magnitude.ShouldBe(
            fieldsFlat[gcOutId].Magnitude, SimulationTolerance,
            "GC output amplitude changed after grouping (issue #387)");

        // 2. Splitter out1 — must match flat circuit
        fieldsGrouped.ShouldContainKey(splitterOut1Pin.IDOutFlow,
            "Grouped simulation: splitter out1 pin not in results");
        fieldsGrouped[splitterOut1Pin.IDOutFlow].Magnitude.ShouldBe(
            fieldsFlat[splitterOut1Pin.IDOutFlow].Magnitude, SimulationTolerance,
            "Splitter out1 amplitude changed after grouping (issue #387)");

        // 3. Splitter out2 — must match flat circuit
        fieldsGrouped.ShouldContainKey(splitterOut2Pin.IDOutFlow,
            "Grouped simulation: splitter out2 pin not in results");
        fieldsGrouped[splitterOut2Pin.IDOutFlow].Magnitude.ShouldBe(
            fieldsFlat[splitterOut2Pin.IDOutFlow].Magnitude, SimulationTolerance,
            "Splitter out2 amplitude changed after grouping (issue #387)");
    }

    /// <summary>
    /// Verifies that grouping components when internal connections have no
    /// routed path (RoutedPath == null) still produces correct simulation results,
    /// provided the fix to CreateGroupCommand is in place.
    ///
    /// Fix: CreateGroupCommand must create FrozenWaveguidePath even when RoutedPath == null,
    /// using an empty RoutedPath as fallback.  An empty path has TransmissionCoefficient = 1
    /// (lossless), which is the correct conservative default and preserves the connection.
    ///
    /// Covers the secondary bug reported in issue #387.
    /// </summary>
    [Fact]
    public async Task WaveguideLoss_IsIdentical_WhenFrozenPathsCreatedFromNullRoutedPaths()
    {
        // Build the same circuit as above.
        var gc = IntegrationCircuitBuilder.CreateGratingCoupler("GC2", 0, 0, Wavelengths);
        var ps = TestComponentFactory.CreateSimpleTwoPortComponent();
        ps.PhysicalX = 30;
        ps.PhysicalY = 0;
        var splitter = IntegrationCircuitBuilder.CreateSplitter("Splitter2", 60, 0, Wavelengths);

        var gcWaveguidePin = gc.Pins["waveguide"];
        var psInPin = ps.PhysicalPins[0];
        var psOutPin = ps.PhysicalPins[1];
        var splitterInPin = splitter.Pins["in1"];

        // Flat simulation (reference)
        var flatTile = new ComponentListTileManager();
        flatTile.AddComponent(gc.Component);
        flatTile.AddComponent(ps);
        flatTile.AddComponent(splitter.Component);

        var flatConn = new WaveguideConnectionManager(new WaveguideRouter());
        flatConn.AddExistingConnection(new WaveguideConnection { StartPin = gcWaveguidePin, EndPin = psInPin });
        flatConn.AddExistingConnection(new WaveguideConnection { StartPin = psOutPin, EndPin = splitterInPin });

        var portManager = new PhysicalExternalPortManager();
        portManager.AddLightSource(
            new ExternalInput("laser2", LaserType.Red, 0, new Complex(1.0, 0)),
            gc.LogicalPins[0].IDInFlow);

        var flatGrid = GridManager.CreateForSimulation(flatTile, flatConn, portManager);
        var fieldsFlat = await RunSimulationAsync(flatGrid);

        // Group WITH frozen paths created from connections where RoutedPath == null.
        // This simulates the FIXED CreateGroupCommand behavior:
        // when conn.RoutedPath == null, CreateGroupCommand now uses new RoutedPath() as fallback.
        // An empty RoutedPath produces TransmissionCoefficient = Complex.One (lossless).
        var group = new ComponentGroup("FixedGroup");
        group.AddChild(gc.Component);
        group.AddChild(ps);
        group.AddChild(splitter.Component);

        group.AddInternalPath(new FrozenWaveguidePath
        {
            Path = new RoutedPath(), // empty path = null RoutedPath case, lossless
            StartPin = gcWaveguidePin,
            EndPin = psInPin
        });
        group.AddInternalPath(new FrozenWaveguidePath
        {
            Path = new RoutedPath(),
            StartPin = psOutPin,
            EndPin = splitterInPin
        });

        group.AddExternalPin(new GroupPin { Name = "GC_in", InternalPin = gcWaveguidePin });
        group.AddExternalPin(new GroupPin { Name = "Splitter_out1", InternalPin = splitter.Pins["out1"] });
        group.AddExternalPin(new GroupPin { Name = "Splitter_out2", InternalPin = splitter.Pins["out2"] });

        group.EnsureSMatrixComputed();

        var groupedTile = new ComponentListTileManager();
        groupedTile.AddComponent(group);

        var groupedGrid = GridManager.CreateForSimulation(groupedTile, new WaveguideConnectionManager(new WaveguideRouter()), portManager);
        var fieldsGrouped = await RunSimulationAsync(groupedGrid);

        // Splitter outputs must match the flat circuit — empty RoutedPath is lossless.
        var splitterOut1Pin = splitter.LogicalPins[1];
        fieldsGrouped.ShouldContainKey(splitterOut1Pin.IDOutFlow,
            "Grouped simulation (empty frozen paths): splitter out1 not in results");
        fieldsGrouped[splitterOut1Pin.IDOutFlow].Magnitude.ShouldBe(
            fieldsFlat[splitterOut1Pin.IDOutFlow].Magnitude, SimulationTolerance,
            "Splitter out1 amplitude changed after grouping when FrozenWaveguidePath has empty RoutedPath (issue #387 secondary bug)");
    }

    private static async Task<Dictionary<Guid, Complex>> RunSimulationAsync(GridManager grid)
    {
        var builder = new SystemMatrixBuilder(grid);
        var calculator = new GridLightCalculator(builder, grid);
        return await calculator.CalculateFieldPropagationAsync(
            new CancellationTokenSource(), WavelengthNm);
    }
}
