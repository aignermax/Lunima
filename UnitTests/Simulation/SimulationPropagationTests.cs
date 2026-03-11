using System.Numerics;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Simulation;

/// <summary>
/// Tests that light propagates through the S-Matrix simulation pipeline.
/// Verifies that light injected at a grating coupler flows through components
/// and appears at downstream connection endpoints.
/// </summary>
public class SimulationPropagationTests
{
    /// <summary>
    /// Creates a simple test: GratingCoupler -> DirectionalCoupler
    /// Verifies that light appears at the DC output pins.
    /// </summary>
    [Fact]
    public async Task LightPropagatesThroughSingleConnection()
    {
        // Create a Grating Coupler (1 pin)
        var gcPin = new Pin("waveguide", 0, MatterType.Light, RectSide.Right);
        var gcParts = new Part[1, 1];
        gcParts[0, 0] = new Part(new List<Pin> { gcPin });

        var gcMatrix = CreateTerminalMatrix(new List<Pin> { gcPin }, 0.3);
        var gcWavelengthMap = new Dictionary<int, SMatrix>
        {
            { 1550, gcMatrix }
        };

        var gcPhysicalPin = new PhysicalPin
        {
            Name = "waveguide",
            OffsetXMicrometers = 30,
            OffsetYMicrometers = 10,
            AngleDegrees = 0,
            LogicalPin = gcPin
        };

        var gc = new Component(
            gcWavelengthMap, new List<Slider>(), "gc", "", gcParts, 0, "GratingCoupler_1",
            DiscreteRotation.R0, new List<PhysicalPin> { gcPhysicalPin });
        gc.PhysicalX = 0;
        gc.PhysicalY = 0;

        // Create a Directional Coupler (4 pins)
        var dcIn1 = new Pin("in1", 0, MatterType.Light, RectSide.Left);
        var dcIn2 = new Pin("in2", 1, MatterType.Light, RectSide.Left);
        var dcOut1 = new Pin("out1", 2, MatterType.Light, RectSide.Right);
        var dcOut2 = new Pin("out2", 3, MatterType.Light, RectSide.Right);
        var dcPins = new List<Pin> { dcIn1, dcIn2, dcOut1, dcOut2 };

        var dcParts = new Part[1, 1];
        dcParts[0, 0] = new Part(dcPins);

        var dcMatrix = CreateCouplerMatrix(dcPins, 0.5);
        var dcWavelengthMap = new Dictionary<int, SMatrix>
        {
            { 1550, dcMatrix }
        };

        var dcPhysicalPins = new List<PhysicalPin>
        {
            new() { Name = "in1", OffsetXMicrometers = 0, OffsetYMicrometers = 3, AngleDegrees = 180, LogicalPin = dcIn1 },
            new() { Name = "in2", OffsetXMicrometers = 0, OffsetYMicrometers = 9, AngleDegrees = 180, LogicalPin = dcIn2 },
            new() { Name = "out1", OffsetXMicrometers = 30, OffsetYMicrometers = 3, AngleDegrees = 0, LogicalPin = dcOut1 },
            new() { Name = "out2", OffsetXMicrometers = 30, OffsetYMicrometers = 9, AngleDegrees = 0, LogicalPin = dcOut2 }
        };

        var dc = new Component(
            dcWavelengthMap, new List<Slider>(), "dc", "", dcParts, 0, "DirectionalCoupler_1",
            DiscreteRotation.R0, dcPhysicalPins);
        dc.PhysicalX = 50;
        dc.PhysicalY = 0;

        // Create connection: GC.waveguide -> DC.in1
        var connection = new WaveguideConnection
        {
            StartPin = gcPhysicalPin,
            EndPin = dcPhysicalPins[0] // DC.in1
        };

        // Set up simulation
        var tileManager = new ComponentListTileManager();
        tileManager.AddComponent(gc);
        tileManager.AddComponent(dc);

        var connectionManager = new WaveguideConnectionManager();
        connectionManager.AddExistingConnection(connection);

        var portManager = new PhysicalExternalPortManager();
        var lightSource = new ExternalInput("src_gc", LaserType.Red, 0, new Complex(1.0, 0));
        portManager.AddLightSource(lightSource, gcPin.IDInFlow);

        var gridManager = GridManager.CreateForSimulation(tileManager, connectionManager, portManager);

        // Build system matrix and check it
        var builder = new SystemMatrixBuilder(gridManager);
        var systemMatrix = builder.GetSystemSMatrix(1550);

        Console.WriteLine($"System matrix size: {systemMatrix.PinReference.Count} pins");
        var nonNull = systemMatrix.GetNonNullValues();
        Console.WriteLine($"Non-null entries: {nonNull.Count}");

        // Verify connection transfer exists in system matrix
        var connTransfers = connectionManager.GetConnectionTransfers();
        Console.WriteLine($"Connection transfers: {connTransfers.Count}");
        foreach (var (key, value) in connTransfers)
        {
            Console.WriteLine($"  {key.PinIdInflow} -> {key.PinIdOutflow} = {value}");
            systemMatrix.PinReference.ShouldContainKey(key.PinIdInflow, "Connection start pin not in system matrix");
            systemMatrix.PinReference.ShouldContainKey(key.PinIdOutflow, "Connection end pin not in system matrix");
        }

        // Verify GC matrix entry exists
        var gcTransfers = gcMatrix.GetNonNullValues();
        Console.WriteLine($"GC matrix transfers: {gcTransfers.Count}");
        gcTransfers.Count.ShouldBe(1, "GC terminal matrix should have 1 entry");

        // Verify DC matrix entries exist
        var dcTransfers = dcMatrix.GetNonNullValues();
        Console.WriteLine($"DC matrix transfers: {dcTransfers.Count}");
        dcTransfers.Count.ShouldBe(8, "DC coupler matrix should have 8 entries (4 forward + 4 reverse)");

        // Verify expected entries in system matrix
        // GC: IDInFlow -> IDOutFlow
        nonNull.ShouldContainKey((gcPin.IDInFlow, gcPin.IDOutFlow), "System matrix missing GC terminal transfer");
        // Connection: GC.IDOutFlow -> DC.in1.IDInFlow
        nonNull.ShouldContainKey((gcPin.IDOutFlow, dcIn1.IDInFlow), "System matrix missing connection transfer");
        // DC: in1.IDInFlow -> out1.IDOutFlow
        nonNull.ShouldContainKey((dcIn1.IDInFlow, dcOut1.IDOutFlow), "System matrix missing DC through transfer");

        // Run simulation
        var calculator = new GridLightCalculator(builder, gridManager);
        var cts = new CancellationTokenSource();
        var fieldResults = await calculator.CalculateFieldPropagationAsync(cts, 1550);

        Console.WriteLine($"\nField results ({fieldResults.Count} entries):");
        foreach (var (pinId, amplitude) in fieldResults)
        {
            if (amplitude.Magnitude > 1e-10)
                Console.WriteLine($"  Pin {pinId}: magnitude={amplitude.Magnitude:F6}");
        }

        // Map pins to names for readability
        Console.WriteLine($"\nPin mapping:");
        Console.WriteLine($"  GC.waveguide.IDInFlow = {gcPin.IDInFlow}");
        Console.WriteLine($"  GC.waveguide.IDOutFlow = {gcPin.IDOutFlow}");
        Console.WriteLine($"  DC.in1.IDInFlow = {dcIn1.IDInFlow}");
        Console.WriteLine($"  DC.in1.IDOutFlow = {dcIn1.IDOutFlow}");
        Console.WriteLine($"  DC.out1.IDInFlow = {dcOut1.IDInFlow}");
        Console.WriteLine($"  DC.out1.IDOutFlow = {dcOut1.IDOutFlow}");
        Console.WriteLine($"  DC.out2.IDOutFlow = {dcOut2.IDOutFlow}");

        // Assert: light should exist at GC output
        fieldResults[gcPin.IDOutFlow].Magnitude.ShouldBeGreaterThan(0, "No light at GC output");

        // Assert: light should propagate through connection to DC input
        fieldResults[dcIn1.IDInFlow].Magnitude.ShouldBeGreaterThan(0, "No light at DC input");

        // Assert: light should propagate through DC to output pins
        fieldResults[dcOut1.IDOutFlow].Magnitude.ShouldBeGreaterThan(0, "No light at DC out1 - propagation failed through component");
        fieldResults[dcOut2.IDOutFlow].Magnitude.ShouldBeGreaterThan(0, "No light at DC out2 - propagation failed through component");
    }

    /// <summary>
    /// Tests that light propagates even when the connection direction is reversed
    /// (user dragged from DC.in1 to GC.waveguide instead of GC to DC).
    /// </summary>
    [Fact]
    public async Task LightPropagatesWithReversedConnectionDirection()
    {
        // Same setup as above but connection is DC.in1 -> GC.waveguide (reversed)
        var gcPin = new Pin("waveguide", 0, MatterType.Light, RectSide.Right);
        var gcParts = new Part[1, 1];
        gcParts[0, 0] = new Part(new List<Pin> { gcPin });
        var gcMatrix = CreateTerminalMatrix(new List<Pin> { gcPin }, 0.3);
        var gcPhysicalPin = new PhysicalPin
        {
            Name = "waveguide", OffsetXMicrometers = 30, OffsetYMicrometers = 10,
            AngleDegrees = 0, LogicalPin = gcPin
        };
        var gc = new Component(
            new Dictionary<int, SMatrix> { { 1550, gcMatrix } },
            new List<Slider>(), "gc", "", gcParts, 0, "GC_1", DiscreteRotation.R0,
            new List<PhysicalPin> { gcPhysicalPin });

        var dcIn1 = new Pin("in1", 0, MatterType.Light, RectSide.Left);
        var dcIn2 = new Pin("in2", 1, MatterType.Light, RectSide.Left);
        var dcOut1 = new Pin("out1", 2, MatterType.Light, RectSide.Right);
        var dcOut2 = new Pin("out2", 3, MatterType.Light, RectSide.Right);
        var dcPins = new List<Pin> { dcIn1, dcIn2, dcOut1, dcOut2 };
        var dcParts = new Part[1, 1];
        dcParts[0, 0] = new Part(dcPins);
        var dcMatrix = CreateCouplerMatrix(dcPins, 0.5);
        var dcPhysicalPins = new List<PhysicalPin>
        {
            new() { Name = "in1", OffsetXMicrometers = 0, OffsetYMicrometers = 3, AngleDegrees = 180, LogicalPin = dcIn1 },
            new() { Name = "in2", OffsetXMicrometers = 0, OffsetYMicrometers = 9, AngleDegrees = 180, LogicalPin = dcIn2 },
            new() { Name = "out1", OffsetXMicrometers = 30, OffsetYMicrometers = 3, AngleDegrees = 0, LogicalPin = dcOut1 },
            new() { Name = "out2", OffsetXMicrometers = 30, OffsetYMicrometers = 9, AngleDegrees = 0, LogicalPin = dcOut2 }
        };
        var dc = new Component(
            new Dictionary<int, SMatrix> { { 1550, dcMatrix } },
            new List<Slider>(), "dc", "", dcParts, 0, "DC_1", DiscreteRotation.R0, dcPhysicalPins);

        // REVERSED connection: DC.in1 -> GC.waveguide (user dragged from DC to GC)
        var connection = new WaveguideConnection
        {
            StartPin = dcPhysicalPins[0], // DC.in1
            EndPin = gcPhysicalPin         // GC.waveguide
        };

        var tileManager = new ComponentListTileManager();
        tileManager.AddComponent(gc);
        tileManager.AddComponent(dc);

        var connectionManager = new WaveguideConnectionManager();
        connectionManager.AddExistingConnection(connection);

        var portManager = new PhysicalExternalPortManager();
        portManager.AddLightSource(new ExternalInput("src_gc", LaserType.Red, 0, new Complex(1.0, 0)), gcPin.IDInFlow);

        var gridManager = GridManager.CreateForSimulation(tileManager, connectionManager, portManager);
        var builder = new SystemMatrixBuilder(gridManager);
        var calculator = new GridLightCalculator(builder, gridManager);
        var cts = new CancellationTokenSource();
        var fieldResults = await calculator.CalculateFieldPropagationAsync(cts, 1550);

        // Light should still propagate through to DC outputs even with reversed connection
        fieldResults[gcPin.IDOutFlow].Magnitude.ShouldBeGreaterThan(0, "No light at GC output");
        fieldResults[dcIn1.IDInFlow].Magnitude.ShouldBeGreaterThan(0, "No light at DC input (reversed connection failed)");
        fieldResults[dcOut1.IDOutFlow].Magnitude.ShouldBeGreaterThan(0, "No light at DC out1 (reversed connection blocked propagation)");
        fieldResults[dcOut2.IDOutFlow].Magnitude.ShouldBeGreaterThan(0, "No light at DC out2 (reversed connection blocked propagation)");
    }

    private static SMatrix CreateTerminalMatrix(List<Pin> pins, double efficiency)
    {
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var sMatrix = new SMatrix(pinIds, new());

        if (pins.Count >= 1)
        {
            var amplitude = new Complex(Math.Sqrt(efficiency), 0);
            var transfers = new Dictionary<(Guid, Guid), Complex>
            {
                { (pins[0].IDInFlow, pins[0].IDOutFlow), amplitude }
            };
            sMatrix.SetValues(transfers);
        }

        return sMatrix;
    }

    private static SMatrix CreateCouplerMatrix(List<Pin> pins, double coupling)
    {
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var sMatrix = new SMatrix(pinIds, new());

        if (pins.Count >= 4)
        {
            var through = new Complex(Math.Sqrt(1 - coupling), 0);
            var cross = new Complex(0, Math.Sqrt(coupling));

            var transfers = new Dictionary<(Guid, Guid), Complex>
            {
                { (pins[0].IDInFlow, pins[2].IDOutFlow), through },
                { (pins[0].IDInFlow, pins[3].IDOutFlow), cross },
                { (pins[1].IDInFlow, pins[2].IDOutFlow), cross },
                { (pins[1].IDInFlow, pins[3].IDOutFlow), through },
                { (pins[2].IDInFlow, pins[0].IDOutFlow), through },
                { (pins[2].IDInFlow, pins[1].IDOutFlow), cross },
                { (pins[3].IDInFlow, pins[0].IDOutFlow), cross },
                { (pins[3].IDInFlow, pins[1].IDOutFlow), through }
            };
            sMatrix.SetValues(transfers);
        }

        return sMatrix;
    }
}
