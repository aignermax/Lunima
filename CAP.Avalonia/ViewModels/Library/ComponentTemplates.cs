using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.FormulaReading;
using CAP_Core.Grid.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using System.Numerics;

namespace CAP.Avalonia.ViewModels.Library;

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
        // Sizes and pin positions match Nazca demofab PDK exactly.
        // Translation from Nazca (Y-up, origin at a0) to our editor (Y-down, top-left origin):
        //   width = bbox.xmax - bbox.xmin
        //   height = bbox.ymax - bbox.ymin
        //   pin offset = (nazca_x - bbox.xmin, bbox.ymax - nazca_y)
        //   origin offset = (0 - bbox.xmin, bbox.ymax - 0) = where Nazca's a0 sits in our coords
        return new List<ComponentTemplate>
        {
            new ComponentTemplate
            {
                // demo.mmi1x2_sh(): bbox (0, -27.5, 80, 27.5) = 80×55
                // Pins: a0=(0,0), b0=(80,2), b1=(80,-2)
                Name = "1x2 MMI Splitter",
                Category = "Splitters",
                WidthMicrometers = 80,
                HeightMicrometers = 55,
                NazcaOriginOffsetX = 0,
                NazcaOriginOffsetY = 27.5,
                PinDefinitions = new[]
                {
                    new PinDefinition("in", 0, 27.5, 180),
                    new PinDefinition("out1", 80, 25.5, 0),
                    new PinDefinition("out2", 80, 29.5, 0)
                },
                CreateSMatrix = pins => CreateSplitterMatrix(pins)
            },
            new ComponentTemplate
            {
                // demo.mmi2x2_dp(): bbox (0, -30, 250, 30) = 250×60
                // Pins: a0=(0,4), a1=(0,-4), b0=(250,4), b1=(250,-4)
                // NazcaOriginOffset points to a0 pin: (0, ymax - a0.y) = (0, 30-4) = (0, 26)
                Name = "2x2 MMI Coupler",
                Category = "Couplers",
                WidthMicrometers = 250,
                HeightMicrometers = 60,
                NazcaOriginOffsetX = 0,
                NazcaOriginOffsetY = 26,
                PinDefinitions = new[]
                {
                    new PinDefinition("in1", 0, 26, 180),
                    new PinDefinition("in2", 0, 34, 180),
                    new PinDefinition("out1", 250, 26, 0),
                    new PinDefinition("out2", 250, 34, 0)
                },
                CreateSMatrix = pins => CreateCouplerMatrix(pins, 0.5)
            },
            new ComponentTemplate
            {
                // demo.mmi2x2_dp(): same physical device as 2x2 MMI Coupler
                // Slider controls coupling ratio κ (0-100%)
                // NazcaOriginOffset points to a0 pin: (0, 26) — same as 2x2 MMI
                Name = "Directional Coupler",
                Category = "Couplers",
                WidthMicrometers = 250,
                HeightMicrometers = 60,
                NazcaOriginOffsetX = 0,
                NazcaOriginOffsetY = 26,
                PinDefinitions = new[]
                {
                    new PinDefinition("in1", 0, 26, 180),
                    new PinDefinition("in2", 0, 34, 180),
                    new PinDefinition("out1", 250, 26, 0),
                    new PinDefinition("out2", 250, 34, 0)
                },
                HasSlider = true,
                SliderMin = 0,
                SliderMax = 100,
                CreateSMatrixWithSliders = (pins, sliders) => CreateDirectionalCouplerMatrix(pins, sliders)
            },
            new ComponentTemplate
            {
                // demo.eopm_dc(length=500): bbox (0, -30, 500, 30) = 500×60
                // Pins: a0=(0,0), b0=(500,0)
                Name = "Phase Shifter",
                Category = "Modulators",
                WidthMicrometers = 500,
                HeightMicrometers = 60,
                NazcaOriginOffsetX = 0,
                NazcaOriginOffsetY = 30,
                PinDefinitions = new[]
                {
                    new PinDefinition("in", 0, 30, 180),
                    new PinDefinition("out", 500, 30, 0)
                },
                HasSlider = true,
                SliderMin = 0,
                SliderMax = 360,
                CreateSMatrixWithSliders = (pins, sliders) => CreatePhaseShifterMatrix(pins, sliders)
            },
            new ComponentTemplate
            {
                // demo.io(): bbox (0, -9.5, 100, 9.5) = 100×19
                // Pins: a0=(0,0) fiber side, b0=(100,0) waveguide side
                Name = "Grating Coupler",
                Category = "I/O",
                WidthMicrometers = 100,
                HeightMicrometers = 19,
                NazcaOriginOffsetX = 0,
                NazcaOriginOffsetY = 9.5,
                PinDefinitions = new[]
                {
                    new PinDefinition("waveguide", 100, 9.5, 0)
                },
                CreateSMatrix = pins => CreateTerminalMatrix(pins, 0.3)
            },
            new ComponentTemplate
            {
                // demo.pd(): bbox (0, -27.5, 70, 27.5) = 70×55
                // Pins: a0=(0,0) input
                Name = "Photodetector",
                Category = "I/O",
                WidthMicrometers = 70,
                HeightMicrometers = 55,
                NazcaOriginOffsetX = 0,
                NazcaOriginOffsetY = 27.5,
                PinDefinitions = new[]
                {
                    new PinDefinition("in", 0, 27.5, 180)
                },
                CreateSMatrix = pins => CreateTerminalMatrix(pins, 0.9)
            },
            new ComponentTemplate
            {
                // demo.mmi1x2_sh(): same physical device as 1x2 MMI Splitter
                Name = "Y-Junction",
                Category = "Splitters",
                WidthMicrometers = 80,
                HeightMicrometers = 55,
                NazcaOriginOffsetX = 0,
                NazcaOriginOffsetY = 27.5,
                PinDefinitions = new[]
                {
                    new PinDefinition("in", 0, 27.5, 180),
                    new PinDefinition("out1", 80, 25.5, 0),
                    new PinDefinition("out2", 80, 29.5, 0)
                },
                CreateSMatrix = pins => CreateSplitterMatrix(pins)
            },
            new ComponentTemplate
            {
                // Ring Resonator: no exact demofab match, use mmi1x2 footprint
                Name = "Ring Resonator",
                Category = "Filters",
                WidthMicrometers = 80,
                HeightMicrometers = 55,
                NazcaOriginOffsetX = 0,
                NazcaOriginOffsetY = 27.5,
                PinDefinitions = new[]
                {
                    new PinDefinition("in", 0, 22.5, 180),
                    new PinDefinition("through", 80, 22.5, 0),
                    new PinDefinition("drop", 80, 47.5, 0)
                },
                CreateSMatrix = pins => CreateRingResonatorMatrix(pins)
            },
            new ComponentTemplate
            {
                // demo.io(): same physical device as Grating Coupler
                Name = "Edge Coupler",
                Category = "I/O",
                WidthMicrometers = 100,
                HeightMicrometers = 19,
                NazcaOriginOffsetX = 0,
                NazcaOriginOffsetY = 9.5,
                PinDefinitions = new[]
                {
                    new PinDefinition("waveguide", 100, 9.5, 0)
                },
                CreateSMatrix = pins => CreateTerminalMatrix(pins, 0.5)
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

        // Create sliders before S-Matrix (slider-aware S-Matrices need slider IDs)
        var sliders = new List<Slider>();
        if (template.HasSlider)
        {
            sliders.Add(new Slider(Guid.NewGuid(), 0, (template.SliderMin + template.SliderMax) / 2, template.SliderMax, template.SliderMin));
        }

        // Create wavelength→S-Matrix map
        Dictionary<int, SMatrix> wavelengthMap;
        if (template.CreateWavelengthSMatrixMap != null)
        {
            // Multi-wavelength: each wavelength has its own S-matrix
            wavelengthMap = template.CreateWavelengthSMatrixMap(logicalPins);
        }
        else
        {
            // Single S-matrix duplicated across standard wavelengths
            SMatrix sMatrix;
            if (template.CreateSMatrixWithSliders != null)
                sMatrix = template.CreateSMatrixWithSliders(logicalPins, sliders);
            else if (template.CreateSMatrix != null)
                sMatrix = template.CreateSMatrix(logicalPins);
            else
                throw new InvalidOperationException($"Template '{template.Name}' has no S-Matrix factory.");

            wavelengthMap = new Dictionary<int, SMatrix>
            {
                { 1550, sMatrix },
                { 1310, sMatrix },
                { 980, sMatrix }
            };
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
        component.NazcaOriginOffsetX = template.NazcaOriginOffsetX;
        component.NazcaOriginOffsetY = template.NazcaOriginOffsetY;
        component.NazcaModuleName = template.NazcaModuleName;

        // Set human-readable display name from the template's display name.
        // This ensures components placed from the library show their PDK display name
        // (e.g., "Grating Coupler TE 1550") rather than their NazcaFunctionName (e.g., "ebeam_gc_te1550")
        // when grouped into prefabs and later instantiated.
        component.HumanReadableName = template.Name;

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

    private static SMatrix CreatePhaseShifterMatrix(List<Pin> pins, List<Slider> sliders)
    {
        // pins: [in, out] - lossless pass-through with slider-controlled phase shift
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var sliderIds = sliders.Select(s => (s.ID, s.Value)).ToList();
        var sMatrix = new SMatrix(pinIds, sliderIds);

        if (pins.Count >= 2 && sliders.Count > 0)
        {
            // Phase shift formula: ToComplexFromPolar(1, SLIDER0 * Pi / 180)
            // Converts degrees (0-360) to radians and applies as phase at unity amplitude
            var phaseFormula = "ToComplexFromPolar(1, SLIDER0 * Pi / 180)";
            var forward = MathExpressionReader.ConvertToDelegate(phaseFormula, pins, sliders);
            var reverse = MathExpressionReader.ConvertToDelegate(phaseFormula, pins, sliders);

            if (forward != null)
                sMatrix.NonLinearConnections[(pins[0].IDInFlow, pins[1].IDOutFlow)] = forward.Value;
            if (reverse != null)
                sMatrix.NonLinearConnections[(pins[1].IDInFlow, pins[0].IDOutFlow)] = reverse.Value;
        }
        else if (pins.Count >= 2)
        {
            // Fallback: unity pass-through if no slider
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

    private static SMatrix CreateDirectionalCouplerMatrix(List<Pin> pins, List<Slider> sliders)
    {
        // pins: [in1, in2, out1, out2]
        // SLIDER0 = coupling ratio κ in percent (0-100)
        // through = sqrt(1 - κ/100), cross = j * sqrt(κ/100)
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var sliderIds = sliders.Select(s => (s.ID, s.Value)).ToList();
        var sMatrix = new SMatrix(pinIds, sliderIds);

        if (pins.Count >= 4 && sliders.Count > 0)
        {
            var throughFormula = "ToComplexFromPolar(Sqrt(1 - SLIDER0 / 100), 0)";
            var crossFormula = "ToComplexFromPolar(Sqrt(SLIDER0 / 100), Pi / 2)";

            // Forward: in1->out1 (through), in1->out2 (cross), in2->out1 (cross), in2->out2 (through)
            var fwdThrough1 = MathExpressionReader.ConvertToDelegate(throughFormula, pins, sliders);
            var fwdCross1 = MathExpressionReader.ConvertToDelegate(crossFormula, pins, sliders);
            var fwdCross2 = MathExpressionReader.ConvertToDelegate(crossFormula, pins, sliders);
            var fwdThrough2 = MathExpressionReader.ConvertToDelegate(throughFormula, pins, sliders);
            // Reverse: out1->in1 (through), out1->in2 (cross), out2->in1 (cross), out2->in2 (through)
            var revThrough1 = MathExpressionReader.ConvertToDelegate(throughFormula, pins, sliders);
            var revCross1 = MathExpressionReader.ConvertToDelegate(crossFormula, pins, sliders);
            var revCross2 = MathExpressionReader.ConvertToDelegate(crossFormula, pins, sliders);
            var revThrough2 = MathExpressionReader.ConvertToDelegate(throughFormula, pins, sliders);

            if (fwdThrough1 != null) sMatrix.NonLinearConnections[(pins[0].IDInFlow, pins[2].IDOutFlow)] = fwdThrough1.Value;
            if (fwdCross1 != null)   sMatrix.NonLinearConnections[(pins[0].IDInFlow, pins[3].IDOutFlow)] = fwdCross1.Value;
            if (fwdCross2 != null)   sMatrix.NonLinearConnections[(pins[1].IDInFlow, pins[2].IDOutFlow)] = fwdCross2.Value;
            if (fwdThrough2 != null) sMatrix.NonLinearConnections[(pins[1].IDInFlow, pins[3].IDOutFlow)] = fwdThrough2.Value;
            if (revThrough1 != null) sMatrix.NonLinearConnections[(pins[2].IDInFlow, pins[0].IDOutFlow)] = revThrough1.Value;
            if (revCross1 != null)   sMatrix.NonLinearConnections[(pins[2].IDInFlow, pins[1].IDOutFlow)] = revCross1.Value;
            if (revCross2 != null)   sMatrix.NonLinearConnections[(pins[3].IDInFlow, pins[0].IDOutFlow)] = revCross2.Value;
            if (revThrough2 != null) sMatrix.NonLinearConnections[(pins[3].IDInFlow, pins[1].IDOutFlow)] = revThrough2.Value;
        }
        else if (pins.Count >= 4)
        {
            // Fallback: 50/50 coupling
            var through = new Complex(Math.Sqrt(0.5), 0);
            var cross = new Complex(0, Math.Sqrt(0.5));
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
    public Func<List<Pin>, SMatrix>? CreateSMatrix { get; set; }
    public Func<List<Pin>, List<Slider>, SMatrix>? CreateSMatrixWithSliders { get; set; }

    /// <summary>
    /// Optional factory for multi-wavelength S-matrices (e.g., from measured .sparam data).
    /// When set, takes precedence over CreateSMatrix for building the wavelength map.
    /// </summary>
    public Func<List<Pin>, Dictionary<int, SMatrix>>? CreateWavelengthSMatrixMap { get; set; }

    /// <summary>
    /// Nazca function name for export (e.g., "pdk.mmi2x2").
    /// If not set, uses a default based on the Name.
    /// </summary>
    public string? NazcaFunctionName { get; set; }

    /// <summary>
    /// Optional Nazca function parameters (e.g., "length=50").
    /// </summary>
    public string? NazcaParameters { get; set; }

    /// <summary>
    /// Identifies which PDK this component comes from (e.g., "SiEPIC EBeam", "Built-in").
    /// </summary>
    public string PdkSource { get; set; } = "Built-in";

    /// <summary>
    /// Offset from our top-left origin to Nazca's component origin (a0 pin position).
    /// Used by the exporter to correctly place components with .put(x, y).
    /// </summary>
    public double NazcaOriginOffsetX { get; set; } = 0;
    public double NazcaOriginOffsetY { get; set; } = 0;

    /// <summary>
    /// Python module name for Nazca import (e.g., "siepic_ebeam_pdk").
    /// </summary>
    public string? NazcaModuleName { get; set; }
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
