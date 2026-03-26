using System.Numerics;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;

namespace UnitTests.Simulation;

/// <summary>
/// Builds test circuits for end-to-end simulation integration tests.
/// Creates components with physical pins and waveguide connections.
/// </summary>
public static class IntegrationCircuitBuilder
{
    private const double GratingCouplerEfficiency = 0.3;
    private const double SplitterCoupling = 0.5;
    private const double DirectionalCouplerCoupling = 0.5;

    /// <summary>
    /// Builds a circuit: GC_in → Splitter → 2x DC → 4x GC_out.
    /// Returns the GridManager, ConnectionManager, and all component pins
    /// needed for simulation and verification.
    /// </summary>
    public static CircuitSetup BuildSplitterToDualCouplerCircuit(int[] wavelengths)
    {
        var gcInput = CreateGratingCoupler("GC_Input", 0, 0, wavelengths);
        var splitter = CreateSplitter("Splitter_1", 50, 0, wavelengths);
        var dcTop = CreateDirectionalCoupler("DC_Top", 120, -15, wavelengths);
        var dcBottom = CreateDirectionalCoupler("DC_Bottom", 120, 15, wavelengths);

        var gcOut1 = CreateGratingCoupler("GC_Out1", 200, -18, wavelengths);
        var gcOut2 = CreateGratingCoupler("GC_Out2", 200, -12, wavelengths);
        var gcOut3 = CreateGratingCoupler("GC_Out3", 200, 12, wavelengths);
        var gcOut4 = CreateGratingCoupler("GC_Out4", 200, 18, wavelengths);

        var tileManager = new ComponentListTileManager();
        foreach (var c in new[] {
            gcInput.Component, splitter.Component,
            dcTop.Component, dcBottom.Component,
            gcOut1.Component, gcOut2.Component,
            gcOut3.Component, gcOut4.Component })
        {
            tileManager.AddComponent(c);
        }

        var connectionManager = new WaveguideConnectionManager(new WaveguideRouter());
        AddConnection(connectionManager, gcInput.Pins["waveguide"], splitter.Pins["in1"]);
        AddConnection(connectionManager, splitter.Pins["out1"], dcTop.Pins["in1"]);
        AddConnection(connectionManager, splitter.Pins["out2"], dcBottom.Pins["in1"]);
        AddConnection(connectionManager, dcTop.Pins["out1"], gcOut1.Pins["waveguide"]);
        AddConnection(connectionManager, dcTop.Pins["out2"], gcOut2.Pins["waveguide"]);
        AddConnection(connectionManager, dcBottom.Pins["out1"], gcOut3.Pins["waveguide"]);
        AddConnection(connectionManager, dcBottom.Pins["out2"], gcOut4.Pins["waveguide"]);

        return new CircuitSetup(
            tileManager, connectionManager,
            gcInput, splitter, dcTop, dcBottom,
            gcOut1, gcOut2, gcOut3, gcOut4);
    }

    /// <summary>
    /// Creates a grating coupler with one optical pin.
    /// </summary>
    public static ComponentInfo CreateGratingCoupler(
        string name, double x, double y, int[] wavelengths)
    {
        var pin = new Pin("waveguide", 0, MatterType.Light, RectSide.Right);
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin> { pin });

        var sMatrixMap = new Dictionary<int, SMatrix>();
        foreach (var wl in wavelengths)
            sMatrixMap[wl] = CreateTerminalMatrix(pin, GratingCouplerEfficiency);

        var physPin = new PhysicalPin
        {
            Name = "waveguide",
            OffsetXMicrometers = 15, OffsetYMicrometers = 5,
            AngleDegrees = 0, LogicalPin = pin
        };

        var component = new Component(
            sMatrixMap, new List<Slider>(), "gc", "", parts, 0, name,
            DiscreteRotation.R0, new List<PhysicalPin> { physPin });
        component.PhysicalX = x;
        component.PhysicalY = y;

        var pins = new Dictionary<string, PhysicalPin> { { "waveguide", physPin } };
        return new ComponentInfo(component, pins, new List<Pin> { pin });
    }

    /// <summary>
    /// Creates a 1x2 splitter with 3 pins: in1, out1, out2.
    /// </summary>
    public static ComponentInfo CreateSplitter(
        string name, double x, double y, int[] wavelengths)
    {
        var inPin = new Pin("in1", 0, MatterType.Light, RectSide.Left);
        var out1 = new Pin("out1", 1, MatterType.Light, RectSide.Right);
        var out2 = new Pin("out2", 2, MatterType.Light, RectSide.Right);
        var allPins = new List<Pin> { inPin, out1, out2 };

        var parts = new Part[1, 1];
        parts[0, 0] = new Part(allPins);

        var sMatrixMap = new Dictionary<int, SMatrix>();
        foreach (var wl in wavelengths)
            sMatrixMap[wl] = CreateSplitterMatrix(allPins, SplitterCoupling);

        var physPins = new List<PhysicalPin>
        {
            new() { Name = "in1", OffsetXMicrometers = 0, OffsetYMicrometers = 5, AngleDegrees = 180, LogicalPin = inPin },
            new() { Name = "out1", OffsetXMicrometers = 30, OffsetYMicrometers = 2, AngleDegrees = 0, LogicalPin = out1 },
            new() { Name = "out2", OffsetXMicrometers = 30, OffsetYMicrometers = 8, AngleDegrees = 0, LogicalPin = out2 }
        };

        var component = new Component(
            sMatrixMap, new List<Slider>(), "splitter", "", parts, 0, name,
            DiscreteRotation.R0, physPins);
        component.PhysicalX = x;
        component.PhysicalY = y;

        var pinDict = physPins.ToDictionary(p => p.Name);
        return new ComponentInfo(component, pinDict, allPins);
    }

    /// <summary>
    /// Creates a directional coupler with 4 pins: in1, in2, out1, out2.
    /// </summary>
    public static ComponentInfo CreateDirectionalCoupler(
        string name, double x, double y, int[] wavelengths)
    {
        var in1 = new Pin("in1", 0, MatterType.Light, RectSide.Left);
        var in2 = new Pin("in2", 1, MatterType.Light, RectSide.Left);
        var out1 = new Pin("out1", 2, MatterType.Light, RectSide.Right);
        var out2 = new Pin("out2", 3, MatterType.Light, RectSide.Right);
        var allPins = new List<Pin> { in1, in2, out1, out2 };

        var parts = new Part[1, 1];
        parts[0, 0] = new Part(allPins);

        var sMatrixMap = new Dictionary<int, SMatrix>();
        foreach (var wl in wavelengths)
            sMatrixMap[wl] = CreateCouplerMatrix(allPins, DirectionalCouplerCoupling);

        var physPins = new List<PhysicalPin>
        {
            new() { Name = "in1", OffsetXMicrometers = 0, OffsetYMicrometers = 3, AngleDegrees = 180, LogicalPin = in1 },
            new() { Name = "in2", OffsetXMicrometers = 0, OffsetYMicrometers = 9, AngleDegrees = 180, LogicalPin = in2 },
            new() { Name = "out1", OffsetXMicrometers = 30, OffsetYMicrometers = 3, AngleDegrees = 0, LogicalPin = out1 },
            new() { Name = "out2", OffsetXMicrometers = 30, OffsetYMicrometers = 9, AngleDegrees = 0, LogicalPin = out2 }
        };

        var component = new Component(
            sMatrixMap, new List<Slider>(), "dc", "", parts, 0, name,
            DiscreteRotation.R0, physPins);
        component.PhysicalX = x;
        component.PhysicalY = y;

        var pinDict = physPins.ToDictionary(p => p.Name);
        return new ComponentInfo(component, pinDict, allPins);
    }

    private static void AddConnection(
        WaveguideConnectionManager manager,
        PhysicalPin start, PhysicalPin end)
    {
        var connection = new WaveguideConnection { StartPin = start, EndPin = end };
        manager.AddExistingConnection(connection);
    }

    private static SMatrix CreateTerminalMatrix(Pin pin, double efficiency)
    {
        var pinIds = new List<Guid> { pin.IDInFlow, pin.IDOutFlow };
        var sMatrix = new SMatrix(pinIds, new());
        var amplitude = new Complex(Math.Sqrt(efficiency), 0);
        sMatrix.SetValues(new Dictionary<(Guid, Guid), Complex>
        {
            { (pin.IDInFlow, pin.IDOutFlow), amplitude }
        });
        return sMatrix;
    }

    /// <summary>
    /// Creates a 1x2 splitter S-matrix. 50/50 split with cross-coupling convention.
    /// pins[0]=in, pins[1]=out1, pins[2]=out2.
    /// </summary>
    private static SMatrix CreateSplitterMatrix(List<Pin> pins, double coupling)
    {
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var sMatrix = new SMatrix(pinIds, new());

        var split = new Complex(Math.Sqrt(coupling), 0);
        var transfers = new Dictionary<(Guid, Guid), Complex>
        {
            // Forward: input → both outputs
            { (pins[0].IDInFlow, pins[1].IDOutFlow), split },
            { (pins[0].IDInFlow, pins[2].IDOutFlow), split },
            // Reverse: outputs → input
            { (pins[1].IDInFlow, pins[0].IDOutFlow), split },
            { (pins[2].IDInFlow, pins[0].IDOutFlow), split }
        };
        sMatrix.SetValues(transfers);
        return sMatrix;
    }

    private static SMatrix CreateCouplerMatrix(List<Pin> pins, double coupling)
    {
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var sMatrix = new SMatrix(pinIds, new());

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
        return sMatrix;
    }
}
