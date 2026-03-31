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
/// Tests that light sources (Grating Couplers) are correctly detected and functional
/// when grouped together with other components.
///
/// Reproduces user-reported issue: "When I group all components including the Grating Coupler,
/// the simulation says 'no light source found'".
/// </summary>
public class GroupedLightSourceDetectionTests
{
    private const int WavelengthNm = 1550;
    private static readonly int[] Wavelengths = { WavelengthNm };
    private const double SimulationTolerance = 1e-10;

    /// <summary>
    /// Creates a circuit: Grating Coupler 1550nm → MMI 1x2 Splitter
    /// Then groups ALL components (including the GC) and runs simulation.
    /// Verifies that:
    /// 1. Light source is detected (no "no light sources found" error)
    /// 2. Light propagates to the MMI outputs
    /// 3. Results match the flat (ungrouped) circuit
    /// </summary>
    [Fact]
    public async Task GroupedGratingCoupler_IsDetectedAsLightSource_AndSimulationWorks()
    {
        // === CREATE COMPONENTS ===
        var gc = IntegrationCircuitBuilder.CreateGratingCoupler("GC_1550", 0, 0, Wavelengths);
        var mmi = IntegrationCircuitBuilder.CreateSplitter("MMI_1x2", 50, 0, Wavelengths);

        var gcWaveguidePin = gc.Pins["waveguide"];
        var mmiInPin = mmi.Pins["in1"];
        var mmiOut1Pin = mmi.Pins["out1"];
        var mmiOut2Pin = mmi.Pins["out2"];

        // === FLAT CIRCUIT (before grouping) ===
        var flatTile = new ComponentListTileManager();
        flatTile.AddComponent(gc.Component);
        flatTile.AddComponent(mmi.Component);

        var flatConn = new WaveguideConnectionManager(new WaveguideRouter());
        flatConn.AddExistingConnection(new WaveguideConnection
        {
            StartPin = gcWaveguidePin,
            EndPin = mmiInPin
        });

        // Configure light source on GC
        var portManager = new PhysicalExternalPortManager();
        portManager.AddLightSource(
            new ExternalInput("laser_1550", LaserType.Red, 0, new Complex(1.0, 0)),
            gc.LogicalPins[0].IDInFlow);

        var flatGrid = GridManager.CreateForSimulation(flatTile, flatConn, portManager);
        var fieldsFlat = await RunSimulationAsync(flatGrid);

        // Verify flat circuit works
        var mmiOut1LogicalPin = mmi.LogicalPins[1];
        var mmiOut2LogicalPin = mmi.LogicalPins[2];
        fieldsFlat[mmiOut1LogicalPin.IDOutFlow].Magnitude
            .ShouldBeGreaterThan(0, "Flat simulation: no light at MMI out1 — circuit setup is broken");
        fieldsFlat[mmiOut2LogicalPin.IDOutFlow].Magnitude
            .ShouldBeGreaterThan(0, "Flat simulation: no light at MMI out2 — circuit setup is broken");

        // === GROUPED CIRCUIT (group ALL components including GC) ===
        var group = new ComponentGroup("TestGroup");
        group.AddChild(gc.Component);
        group.AddChild(mmi.Component);

        // Frozen path for internal connection
        group.AddInternalPath(new FrozenWaveguidePath
        {
            Path = new RoutedPath(),
            StartPin = gcWaveguidePin,
            EndPin = mmiInPin
        });

        // Expose GC input and MMI outputs as external pins
        group.AddExternalPin(new GroupPin
        {
            Name = "GC_input",
            InternalPin = gcWaveguidePin,
            RelativeX = 0,
            RelativeY = 0,
            AngleDegrees = 0
        });
        group.AddExternalPin(new GroupPin
        {
            Name = "MMI_out1",
            InternalPin = mmiOut1Pin,
            RelativeX = 50,
            RelativeY = -3,
            AngleDegrees = 0
        });
        group.AddExternalPin(new GroupPin
        {
            Name = "MMI_out2",
            InternalPin = mmiOut2Pin,
            RelativeX = 50,
            RelativeY = 3,
            AngleDegrees = 0
        });

        group.EnsureSMatrixComputed();

        // Create grid with only the group
        var groupedTile = new ComponentListTileManager();
        groupedTile.AddComponent(group);

        var emptyConn = new WaveguideConnectionManager(new WaveguideRouter());

        // IMPORTANT: Reuse the same portManager - light source is still attached to internal GC pin
        var groupedGrid = GridManager.CreateForSimulation(groupedTile, emptyConn, portManager);

        // === RUN SIMULATION ON GROUPED CIRCUIT ===
        var fieldsGrouped = await RunSimulationAsync(groupedGrid);

        // === VERIFY: Light source is detected and simulation works ===

        // 1. Verify light reaches GC output (still accessible via internal pin ID)
        var gcOutId = gc.LogicalPins[0].IDOutFlow;
        fieldsGrouped.ShouldContainKey(gcOutId,
            "FAILED: GC output pin not found in grouped simulation results. " +
            "This indicates the light source was not detected when GC is inside a group!");

        fieldsGrouped[gcOutId].Magnitude.ShouldBeGreaterThan(0,
            "FAILED: No light at GC output in grouped simulation. " +
            "Light source inside group was not propagating light!");

        // 2. Verify light reaches MMI outputs
        fieldsGrouped.ShouldContainKey(mmiOut1LogicalPin.IDOutFlow,
            "FAILED: MMI out1 pin not found in grouped simulation results");
        fieldsGrouped[mmiOut1LogicalPin.IDOutFlow].Magnitude.ShouldBeGreaterThan(0,
            "FAILED: No light at MMI out1 in grouped simulation");

        fieldsGrouped.ShouldContainKey(mmiOut2LogicalPin.IDOutFlow,
            "FAILED: MMI out2 pin not found in grouped simulation results");
        fieldsGrouped[mmiOut2LogicalPin.IDOutFlow].Magnitude.ShouldBeGreaterThan(0,
            "FAILED: No light at MMI out2 in grouped simulation");

        // 3. Verify amplitudes match flat circuit (grouping should not change simulation results)
        fieldsGrouped[gcOutId].Magnitude.ShouldBe(
            fieldsFlat[gcOutId].Magnitude, SimulationTolerance,
            "GC output amplitude changed after grouping");

        fieldsGrouped[mmiOut1LogicalPin.IDOutFlow].Magnitude.ShouldBe(
            fieldsFlat[mmiOut1LogicalPin.IDOutFlow].Magnitude, SimulationTolerance,
            "MMI out1 amplitude changed after grouping");

        fieldsGrouped[mmiOut2LogicalPin.IDOutFlow].Magnitude.ShouldBe(
            fieldsFlat[mmiOut2LogicalPin.IDOutFlow].Magnitude, SimulationTolerance,
            "MMI out2 amplitude changed after grouping");
    }

    private static async Task<Dictionary<Guid, Complex>> RunSimulationAsync(GridManager grid)
    {
        var builder = new SystemMatrixBuilder(grid);
        var calculator = new GridLightCalculator(builder, grid);
        return await calculator.CalculateFieldPropagationAsync(
            new CancellationTokenSource(), WavelengthNm);
    }
}
