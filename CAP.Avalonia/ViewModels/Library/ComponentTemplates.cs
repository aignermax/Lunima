using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Library;

public static class ComponentTemplates
{
    private static int _componentCounter = 0;

    /// <summary>
    /// Advances the instance counter past the numeric suffix of an identifier already in use
    /// ("MMI_3765" reserves 3765), so components created afterwards — placed by hand or by the
    /// crossing pass — never reuse the identifier of a component loaded from a file. Saved
    /// connections reference components by identifier, so a collision rewires the design on
    /// the next load.
    /// </summary>
    /// <param name="identifier">An identifier present in the design.</param>
    public static void ReserveIdentifier(string? identifier)
    {
        if (identifier == null) return;
        int separator = identifier.LastIndexOf('_');
        if (separator < 0 || !int.TryParse(identifier.AsSpan(separator + 1), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int suffix))
            return;
        int current;
        do
        {
            current = Volatile.Read(ref _componentCounter);
            if (suffix <= current) return;
        }
        while (Interlocked.CompareExchange(ref _componentCounter, suffix, current) != current);
    }

    [Obsolete("Use JSON PDK files (demo-pdk.json, siepic-ebeam-pdk.json) as the source of component templates. This method returns an empty list.")]
    public static List<ComponentTemplate> GetAllTemplates() => new List<ComponentTemplate>();

    public static Component CreateFromTemplate(ComponentTemplate template, double x, double y)
    {
        // Interlocked: the crossing pass creates components on the routing thread.
        var instanceName = $"{template.Name}_{Interlocked.Increment(ref _componentCounter)}";

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
            logicalPins.Add(new Pin(def.Name, i, def.Kind, side) { Polarization = def.Polarization });
        }

        var parts = new Part[1, 1];
        parts[0, 0] = new Part(logicalPins);

        var sliders = new List<Slider>();
        if (template.SliderDefinitions.Count > 0)
        {
            foreach (var def in template.SliderDefinitions)
                sliders.Add(new Slider(Guid.NewGuid(), def.Number, def.InitialValue, def.Max, def.Min));
        }
        else if (template.HasSlider)
        {
            sliders.Add(new Slider(Guid.NewGuid(), 0, (template.SliderMin + template.SliderMax) / 2, template.SliderMax, template.SliderMin));
        }

        Dictionary<int, SMatrix> wavelengthMap;
        if (template.CreateWavelengthSMatrixMap != null)
        {
            wavelengthMap = template.CreateWavelengthSMatrixMap(logicalPins);
        }
        else
        {
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
                LogicalPin = logicalPins[i],
                WaveguideWidthMicrometers = def.WaveguideWidthMicrometers,
                Layer = def.Layer
            });
        }

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
        component.GdsFactoryFunction = template.GdsFactoryFunction;
        component.GdsFactoryRoutingCrossSection = template.GdsFactoryRoutingCrossSection;

        component.HumanReadableName = template.Name;
        component.TemplateName = template.Name;
        component.WaveguideDispersion = template.WaveguideDispersion;
        component.ParameterDefinitions = template.ParameterDefinitions;
        component.OutlinePolygons = template.OutlinePolygons;

        // The Component constructor resets every slider to its range midpoint;
        // restore the template-defined initial values (parameter defaults) so a
        // freshly placed parametric component starts at its documented default.
        foreach (var def in template.SliderDefinitions)
        {
            var slider = component.GetSlider(def.Number);
            if (slider != null)
                slider.Value = def.InitialValue;
        }

        return component;
    }

}

public partial class ComponentTemplate : ObservableObject
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public double WidthMicrometers { get; set; }
    public double HeightMicrometers { get; set; }
    public PinDefinition[] PinDefinitions { get; set; } = Array.Empty<PinDefinition>();
    public bool HasSlider { get; set; }
    public double SliderMin { get; set; }
    public double SliderMax { get; set; }

    /// <summary>
    /// All slider definitions of the component. Supersedes the legacy
    /// single-slider fields above for multi-parameter components; when empty,
    /// <see cref="HasSlider"/>/<see cref="SliderMin"/>/<see cref="SliderMax"/>
    /// still describe the only slider.
    /// </summary>
    public IReadOnlyList<SliderDefinition> SliderDefinitions { get; set; } = Array.Empty<SliderDefinition>();

    /// <summary>
    /// Physical parameter metadata (labels, units, ranges, slider bindings) for
    /// parametric components; empty otherwise. Copied onto every placed
    /// instance so the properties panel can render named parameter editors.
    /// </summary>
    public IReadOnlyList<CAP_Core.Components.Parametric.ParameterDefinition> ParameterDefinitions { get; set; }
        = Array.Empty<CAP_Core.Components.Parametric.ParameterDefinition>();

    [ObservableProperty]
    private bool _hasUserGlobalSMatrixOverride;

    /// <summary>
    /// Whether the library shows the inline delete/restore ✕. Recomputed once per library
    /// change by <c>LeftPanelViewModel.RefreshTemplateDeletableFlags</c>, never per binding.
    /// </summary>
    [ObservableProperty]
    private bool _isDeletable;

    public Func<List<Pin>, SMatrix>? CreateSMatrix { get; set; }
    public Func<List<Pin>, List<Slider>, SMatrix>? CreateSMatrixWithSliders { get; set; }

    public Func<List<Pin>, Dictionary<int, SMatrix>>? CreateWavelengthSMatrixMap { get; set; }

    public string? NazcaFunctionName { get; set; }

    public string? NazcaParameters { get; set; }

    public string PdkSource { get; set; } = "Built-in";

    /// <summary>
    /// Wavelength-dependent waveguide dispersion of this template's PDK (root
    /// <c>materialDispersion</c>, or the component's own block when it declares one),
    /// resolved to a domain model. Stamped onto every placed instance as
    /// <see cref="Component.WaveguideDispersion"/> so routed waveguide connections
    /// inherit the PDK's n_eff(λ)/n_g(λ). Null when the PDK declares no dispersion.
    /// </summary>
    public CAP_Core.LightCalculation.MaterialDispersion.IDispersionModel? WaveguideDispersion { get; set; }

    public double NazcaOriginOffsetX { get; set; } = 0;
    public double NazcaOriginOffsetY { get; set; } = 0;

    public string? NazcaModuleName { get; set; }

    public string? GdsFactoryFunction { get; set; }

    public string? GdsFactoryRoutingCrossSection { get; set; }

    public string? RawCode { get; set; }

    public string? RawCodeBackend { get; set; }

    /// <summary>
    /// Imported outline polygons of the component shape (e.g. from a GDS-imported
    /// PDK component), in app-space µm (Y-down, relative to the bbox top-left).
    /// <c>null</c> for regular components — the canvas then draws the plain
    /// rectangle body. The list instance is shared with every placed component.
    /// </summary>
    public IReadOnlyList<CAP_Core.Components.Core.OutlinePolygon>? OutlinePolygons { get; set; }

    public bool IsCustom { get; set; }

    /// <summary>
    /// The PDK component draft this template was converted from, used to detect divergence
    /// from a bundled original without re-parsing any file.
    /// </summary>
    public CAP_DataAccess.Components.ComponentDraftMapper.DTOs.PdkComponentDraft? SourceDraft { get; set; }
}

/// <summary>
/// Template-level description of one slider: its index, range, and the value a
/// freshly placed instance starts at (the bound parameter's default, when any).
/// </summary>
public record SliderDefinition(int Number, double Min, double Max, double InitialValue);

public class PinDefinition
{
    public string Name { get; }
    public double OffsetX { get; }
    public double OffsetY { get; }
    public double AngleDegrees { get; }

    public MatterType Kind { get; }

    public PolarizationKind Polarization { get; }

    /// <summary>
    /// Waveguide width in µm at this pin from the PDK (per-pin value or the process'
    /// default optical cross-section); null when the PDK declares neither — the
    /// pin-mismatch rule then stays silent for this pin.
    /// </summary>
    public double? WaveguideWidthMicrometers { get; }

    /// <summary>GDS layer number of this pin's waveguide from the PDK; null when undeclared.</summary>
    public int? Layer { get; }

    public PinDefinition(string name, double offsetX, double offsetY, double angleDegrees,
        MatterType kind = MatterType.Light, PolarizationKind polarization = PolarizationKind.TE,
        double? waveguideWidthMicrometers = null, int? layer = null)
    {
        Name = name;
        OffsetX = offsetX;
        OffsetY = offsetY;
        AngleDegrees = angleDegrees;
        Kind = kind;
        Polarization = polarization;
        WaveguideWidthMicrometers = waveguideWidthMicrometers;
        Layer = layer;
    }
}
