using CAP_Core.Components;
using CAP_Core.Components.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using System.Numerics;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// Defines available component templates for the component library.
/// </summary>
public static class ComponentTemplates
{
    private static int _componentCounter = 0;

    public static List<ComponentTemplate> GetAllTemplates()
    {
        // Realistic component sizes based on typical silicon photonics foundry PDKs
        // Waveguide routing is automatic - no need for manual waveguide/bend components
        return new List<ComponentTemplate>
        {
            new ComponentTemplate
            {
                // 1x2 MMI Splitter: typical length 5-15µm, width ~6µm for the MMI section
                // Total footprint including tapers: ~20×15µm
                Name = "1x2 MMI Splitter",
                Category = "Splitters",
                WidthMicrometers = 20,
                HeightMicrometers = 15,
                PinDefinitions = new[]
                {
                    new PinDefinition("in", 0, 7.5, 180),  // Left side, pin points west (away from center)
                    new PinDefinition("out1", 20, 4, 0),   // Right side, pin points east (away from center)
                    new PinDefinition("out2", 20, 11, 0)   // Right side, pin points east (away from center)
                },
                CreateSMatrix = pins => CreateSplitterMatrix(pins)
            },
            new ComponentTemplate
            {
                // 2x2 MMI Coupler: typical length 30-50µm, width ~6µm
                // Total footprint: ~50×20µm
                Name = "2x2 MMI Coupler",
                Category = "Couplers",
                WidthMicrometers = 50,
                HeightMicrometers = 20,
                PinDefinitions = new[]
                {
                    new PinDefinition("in1", 0, 5, 180),    // Left side, pin points west (away from center)
                    new PinDefinition("in2", 0, 15, 180),   // Left side, pin points west (away from center)
                    new PinDefinition("out1", 50, 5, 0),    // Right side, pin points east (away from center)
                    new PinDefinition("out2", 50, 15, 0)    // Right side, pin points east (away from center)
                },
                CreateSMatrix = pins => CreateCouplerMatrix(pins, 0.5) // 50/50 coupling
            },
            new ComponentTemplate
            {
                // Directional Coupler: more compact than MMI
                // Typical: 10-20µm coupling length, ~10µm total width
                Name = "Directional Coupler",
                Category = "Couplers",
                WidthMicrometers = 30,
                HeightMicrometers = 12,
                PinDefinitions = new[]
                {
                    new PinDefinition("in1", 0, 3, 180),  // Left side, pin points west (away from center)
                    new PinDefinition("in2", 0, 9, 180),  // Left side, pin points west (away from center)
                    new PinDefinition("out1", 30, 3, 0),  // Right side, pin points east (away from center)
                    new PinDefinition("out2", 30, 9, 0)   // Right side, pin points east (away from center)
                },
                CreateSMatrix = pins => CreateCouplerMatrix(pins, 0.5)
            },
            new ComponentTemplate
            {
                // Thermo-optic Phase Shifter: 100-300µm heater length, ~5µm wide
                // Total footprint: ~200×10µm
                Name = "Phase Shifter",
                Category = "Modulators",
                WidthMicrometers = 200,
                HeightMicrometers = 10,
                PinDefinitions = new[]
                {
                    new PinDefinition("in", 0, 5, 180),   // Left side, pin points west (away from center)
                    new PinDefinition("out", 200, 5, 0)   // Right side, pin points east (away from center)
                },
                HasSlider = true,
                SliderMin = 0,
                SliderMax = 360,
                CreateSMatrix = pins => CreatePhaseShifterMatrix(pins)
            },
            new ComponentTemplate
            {
                // Grating Coupler: ~15×15µm grating area + taper
                // Total footprint: ~30×20µm
                Name = "Grating Coupler",
                Category = "I/O",
                WidthMicrometers = 30,
                HeightMicrometers = 20,
                PinDefinitions = new[]
                {
                    new PinDefinition("waveguide", 30, 10, 0)
                },
                CreateSMatrix = pins => CreateTerminalMatrix(pins, 0.3) // ~30% coupling efficiency
            },
            new ComponentTemplate
            {
                // Ge Photodetector: 10-50µm length, ~5µm wide
                // Total footprint: ~40×15µm
                Name = "Photodetector",
                Category = "I/O",
                WidthMicrometers = 40,
                HeightMicrometers = 15,
                PinDefinitions = new[]
                {
                    new PinDefinition("in", 0, 7.5, 180)
                },
                CreateSMatrix = pins => CreateTerminalMatrix(pins, 0.9) // ~90% absorption
            },
            new ComponentTemplate
            {
                // Y-Junction: compact alternative to MMI
                // Typical: ~5×10µm
                Name = "Y-Junction",
                Category = "Splitters",
                WidthMicrometers = 10,
                HeightMicrometers = 12,
                PinDefinitions = new[]
                {
                    new PinDefinition("in", 0, 6, 180),  // Left side, pin points west (away from center)
                    new PinDefinition("out1", 10, 3, 0), // Right side, pin points east (away from center)
                    new PinDefinition("out2", 10, 9, 0)  // Right side, pin points east (away from center)
                },
                CreateSMatrix = pins => CreateSplitterMatrix(pins)
            },
            new ComponentTemplate
            {
                // Ring Resonator: radius ~5-10µm, coupling region
                // Total footprint: ~30×25µm
                Name = "Ring Resonator",
                Category = "Filters",
                WidthMicrometers = 30,
                HeightMicrometers = 25,
                PinDefinitions = new[]
                {
                    new PinDefinition("in", 0, 5, 180),      // Left side, pin points west (away from center)
                    new PinDefinition("through", 30, 5, 0),  // Right side, pin points east (away from center)
                    new PinDefinition("drop", 30, 20, 0)     // Right side, pin points east (away from center)
                },
                CreateSMatrix = pins => CreateRingResonatorMatrix(pins)
            },
            new ComponentTemplate
            {
                // Edge Coupler for fiber coupling
                // Taper length: 100-300µm
                Name = "Edge Coupler",
                Category = "I/O",
                WidthMicrometers = 150,
                HeightMicrometers = 15,
                PinDefinitions = new[]
                {
                    new PinDefinition("waveguide", 150, 7.5, 0)
                },
                CreateSMatrix = pins => CreateTerminalMatrix(pins, 0.5) // ~50% coupling
            }
        };
    }

    public static Component CreateFromTemplate(ComponentTemplate template, double x, double y)
    {
        _componentCounter++;
        var instanceName = $"{template.Name}_{_componentCounter}";

        // Create logical pins
        var logicalPins = new List<Pin>();
        for (int i = 0; i < template.PinDefinitions.Length; i++)
        {
            var def = template.PinDefinitions[i];
            var side = def.AngleDegrees switch
            {
                0 => RectSide.Right,
                90 => RectSide.Up,
                180 => RectSide.Left,
                270 => RectSide.Down,
                _ => RectSide.Right
            };
            logicalPins.Add(new Pin(def.Name, i, MatterType.Light, side));
        }

        // Create parts array (simplified: single part)
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(logicalPins);

        // Create S-Matrix
        var sMatrix = template.CreateSMatrix(logicalPins);
        var wavelengthMap = new Dictionary<int, SMatrix>
        {
            { 1550, sMatrix },
            { 1310, sMatrix },
            { 980, sMatrix }
        };

        // Create sliders if needed
        var sliders = new List<Slider>();
        if (template.HasSlider)
        {
            sliders.Add(new Slider(Guid.NewGuid(), 0, (template.SliderMin + template.SliderMax) / 2, template.SliderMax, template.SliderMin));
        }

        // Create physical pins linked to logical pins
        var physicalPins = new List<PhysicalPin>();
        for (int i = 0; i < template.PinDefinitions.Length; i++)
        {
            var def = template.PinDefinitions[i];
            physicalPins.Add(new PhysicalPin
            {
                Name = def.Name,
                OffsetXMicrometers = def.OffsetX,
                OffsetYMicrometers = def.OffsetY,
                AngleDegrees = def.AngleDegrees,
                LogicalPin = logicalPins[i]
            });
        }

        // Use explicit NazcaFunctionName if set, otherwise generate from name
        var nazcaFunction = template.NazcaFunctionName
            ?? $"nazca_{template.Name.ToLower().Replace(" ", "_")}";
        var nazcaParams = template.NazcaParameters ?? "";

        var component = new Component(
            wavelengthMap,
            sliders,
            nazcaFunction,
            nazcaParams,
            parts,
            0,
            instanceName,
            DiscreteRotation.R0,
            physicalPins);

        component.PhysicalX = x;
        component.PhysicalY = y;
        component.WidthMicrometers = template.WidthMicrometers;
        component.HeightMicrometers = template.HeightMicrometers;

        return component;
    }

    private static SMatrix CreatePassThroughMatrix(List<Pin> pins, double transmission)
    {
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var sMatrix = new SMatrix(pinIds, new());

        if (pins.Count >= 2)
        {
            var amplitude = new Complex(Math.Sqrt(transmission), 0);
            var transfers = new Dictionary<(Guid, Guid), Complex>
            {
                { (pins[0].IDInFlow, pins[1].IDOutFlow), amplitude },
                { (pins[1].IDInFlow, pins[0].IDOutFlow), amplitude }
            };
            sMatrix.SetValues(transfers);
        }

        return sMatrix;
    }

    private static SMatrix CreateSplitterMatrix(List<Pin> pins)
    {
        // pins: [in, out1, out2]
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var sMatrix = new SMatrix(pinIds, new());

        if (pins.Count >= 3)
        {
            // 1x2 splitter: 50/50 split (3dB per arm), reciprocal
            var amplitude = new Complex(Math.Sqrt(0.5), 0);
            var transfers = new Dictionary<(Guid, Guid), Complex>
            {
                // Forward: in -> out1, out2
                { (pins[0].IDInFlow, pins[1].IDOutFlow), amplitude },
                { (pins[0].IDInFlow, pins[2].IDOutFlow), amplitude },
                // Reverse: out1, out2 -> in (reciprocal)
                { (pins[1].IDInFlow, pins[0].IDOutFlow), amplitude },
                { (pins[2].IDInFlow, pins[0].IDOutFlow), amplitude }
            };
            sMatrix.SetValues(transfers);
        }

        return sMatrix;
    }

    private static SMatrix CreateCouplerMatrix(List<Pin> pins, double coupling)
    {
        // pins: [in1, in2, out1, out2]
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var sMatrix = new SMatrix(pinIds, new());

        if (pins.Count >= 4)
        {
            // Standard 2x2 coupler: through = sqrt(1-κ), cross = j*sqrt(κ)
            var through = new Complex(Math.Sqrt(1 - coupling), 0);
            var cross = new Complex(0, Math.Sqrt(coupling)); // 90° phase shift on cross port

            var transfers = new Dictionary<(Guid, Guid), Complex>
            {
                // Forward
                { (pins[0].IDInFlow, pins[2].IDOutFlow), through }, // in1 -> out1 (through)
                { (pins[0].IDInFlow, pins[3].IDOutFlow), cross },   // in1 -> out2 (cross)
                { (pins[1].IDInFlow, pins[2].IDOutFlow), cross },   // in2 -> out1 (cross)
                { (pins[1].IDInFlow, pins[3].IDOutFlow), through }, // in2 -> out2 (through)
                // Reverse (reciprocal)
                { (pins[2].IDInFlow, pins[0].IDOutFlow), through },
                { (pins[2].IDInFlow, pins[1].IDOutFlow), cross },
                { (pins[3].IDInFlow, pins[0].IDOutFlow), cross },
                { (pins[3].IDInFlow, pins[1].IDOutFlow), through }
            };
            sMatrix.SetValues(transfers);
        }

        return sMatrix;
    }

    private static SMatrix CreatePhaseShifterMatrix(List<Pin> pins)
    {
        // pins: [in, out] - lossless pass-through (phase handled by slider/nonlinear)
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var sMatrix = new SMatrix(pinIds, new());

        if (pins.Count >= 2)
        {
            var unity = new Complex(1.0, 0);
            var transfers = new Dictionary<(Guid, Guid), Complex>
            {
                { (pins[0].IDInFlow, pins[1].IDOutFlow), unity },
                { (pins[1].IDInFlow, pins[0].IDOutFlow), unity }
            };
            sMatrix.SetValues(transfers);
        }

        return sMatrix;
    }

    private static SMatrix CreateTerminalMatrix(List<Pin> pins, double efficiency)
    {
        // Terminal component (grating coupler, detector) - single pin
        // Light entering IDInFlow exits through IDOutFlow with coupling efficiency
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

    private static SMatrix CreateRingResonatorMatrix(List<Pin> pins)
    {
        // pins: [in, through, drop]
        // Simplified model: 70% through, 25% drop, 5% loss
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var sMatrix = new SMatrix(pinIds, new());

        if (pins.Count >= 3)
        {
            var throughAmp = new Complex(Math.Sqrt(0.7), 0);
            var dropAmp = new Complex(Math.Sqrt(0.25), 0);

            var transfers = new Dictionary<(Guid, Guid), Complex>
            {
                // Forward
                { (pins[0].IDInFlow, pins[1].IDOutFlow), throughAmp },
                { (pins[0].IDInFlow, pins[2].IDOutFlow), dropAmp },
                // Reverse (reciprocal)
                { (pins[1].IDInFlow, pins[0].IDOutFlow), throughAmp },
                { (pins[2].IDInFlow, pins[0].IDOutFlow), dropAmp }
            };
            sMatrix.SetValues(transfers);
        }

        return sMatrix;
    }
}

public class ComponentTemplate
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public double WidthMicrometers { get; set; }
    public double HeightMicrometers { get; set; }
    public PinDefinition[] PinDefinitions { get; set; } = Array.Empty<PinDefinition>();
    public bool HasSlider { get; set; }
    public double SliderMin { get; set; }
    public double SliderMax { get; set; }
    public Func<List<Pin>, SMatrix> CreateSMatrix { get; set; } = _ => null!;

    /// <summary>
    /// Nazca function name for export (e.g., "pdk.mmi2x2").
    /// If not set, uses a default based on the Name.
    /// </summary>
    public string? NazcaFunctionName { get; set; }

    /// <summary>
    /// Optional Nazca function parameters (e.g., "length=50").
    /// </summary>
    public string? NazcaParameters { get; set; }
}

public class PinDefinition
{
    public string Name { get; }
    public double OffsetX { get; }
    public double OffsetY { get; }
    public double AngleDegrees { get; }

    public PinDefinition(string name, double offsetX, double offsetY, double angleDegrees)
    {
        Name = name;
        OffsetX = offsetX;
        OffsetY = offsetY;
        AngleDegrees = angleDegrees;
    }
}
