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
        return new List<ComponentTemplate>
        {
            new ComponentTemplate
            {
                Name = "Straight Waveguide",
                Category = "Basic",
                WidthMicrometers = 250,
                HeightMicrometers = 250,
                PinDefinitions = new[]
                {
                    new PinDefinition("in", 0, 125, 180),
                    new PinDefinition("out", 250, 125, 0)
                },
                CreateSMatrix = pins => CreatePassThroughMatrix(pins, 0.98) // 2% loss
            },
            new ComponentTemplate
            {
                Name = "1x2 Splitter",
                Category = "Splitters",
                WidthMicrometers = 250,
                HeightMicrometers = 500,
                PinDefinitions = new[]
                {
                    new PinDefinition("in", 0, 250, 180),
                    new PinDefinition("out1", 250, 125, 0),
                    new PinDefinition("out2", 250, 375, 0)
                },
                CreateSMatrix = pins => CreateSplitterMatrix(pins)
            },
            new ComponentTemplate
            {
                Name = "2x2 Coupler",
                Category = "Couplers",
                WidthMicrometers = 500,
                HeightMicrometers = 500,
                PinDefinitions = new[]
                {
                    new PinDefinition("in1", 0, 125, 180),
                    new PinDefinition("in2", 0, 375, 180),
                    new PinDefinition("out1", 500, 125, 0),
                    new PinDefinition("out2", 500, 375, 0)
                },
                CreateSMatrix = pins => CreateCouplerMatrix(pins, 0.5) // 50/50 coupling
            },
            new ComponentTemplate
            {
                Name = "Phase Shifter",
                Category = "Modulators",
                WidthMicrometers = 500,
                HeightMicrometers = 250,
                PinDefinitions = new[]
                {
                    new PinDefinition("in", 0, 125, 180),
                    new PinDefinition("out", 500, 125, 0)
                },
                HasSlider = true,
                SliderMin = 0,
                SliderMax = 360,
                CreateSMatrix = pins => CreatePhaseShifterMatrix(pins)
            },
            new ComponentTemplate
            {
                Name = "Grating Coupler",
                Category = "I/O",
                WidthMicrometers = 250,
                HeightMicrometers = 250,
                PinDefinitions = new[]
                {
                    new PinDefinition("waveguide", 250, 125, 0)
                },
                CreateSMatrix = pins => CreateTerminalMatrix(pins, 0.3) // 30% coupling efficiency
            },
            new ComponentTemplate
            {
                Name = "Photodetector",
                Category = "I/O",
                WidthMicrometers = 250,
                HeightMicrometers = 250,
                PinDefinitions = new[]
                {
                    new PinDefinition("in", 0, 125, 180)
                },
                CreateSMatrix = pins => CreateTerminalMatrix(pins, 0.9) // 90% absorption
            },
            new ComponentTemplate
            {
                Name = "Y-Junction",
                Category = "Splitters",
                WidthMicrometers = 400,
                HeightMicrometers = 400,
                PinDefinitions = new[]
                {
                    new PinDefinition("in", 0, 200, 180),
                    new PinDefinition("out1", 400, 100, 0),
                    new PinDefinition("out2", 400, 300, 0)
                },
                CreateSMatrix = pins => CreateSplitterMatrix(pins)
            },
            new ComponentTemplate
            {
                Name = "Bend 90°",
                Category = "Basic",
                WidthMicrometers = 250,
                HeightMicrometers = 250,
                PinDefinitions = new[]
                {
                    new PinDefinition("in", 0, 125, 180),
                    new PinDefinition("out", 125, 250, 90)
                },
                CreateSMatrix = pins => CreatePassThroughMatrix(pins, 0.99) // 1% bend loss
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
        var connections = new List<(Guid, double)>();

        if (pins.Count >= 2)
        {
            // Simple pass-through: in -> out with given transmission
            var amplitude = Math.Sqrt(transmission);
            // Connect inflow of pin0 to outflow of pin1
            // This is simplified - real S-matrix would be more complex
        }

        return new SMatrix(pinIds, connections);
    }

    private static SMatrix CreateSplitterMatrix(List<Pin> pins)
    {
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var connections = new List<(Guid, double)>();

        // 1x2 splitter: input splits equally to both outputs
        // For simplicity, just create the ID list
        return new SMatrix(pinIds, connections);
    }

    private static SMatrix CreateCouplerMatrix(List<Pin> pins, double coupling)
    {
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var connections = new List<(Guid, double)>();

        // 2x2 coupler matrix - for now simplified
        return new SMatrix(pinIds, connections);
    }

    private static SMatrix CreatePhaseShifterMatrix(List<Pin> pins)
    {
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var connections = new List<(Guid, double)>();

        // Phase shifter - transmission with adjustable phase
        return new SMatrix(pinIds, connections);
    }

    private static SMatrix CreateTerminalMatrix(List<Pin> pins, double efficiency)
    {
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var connections = new List<(Guid, double)>();

        // Terminal component (grating coupler, detector)
        return new SMatrix(pinIds, connections);
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
