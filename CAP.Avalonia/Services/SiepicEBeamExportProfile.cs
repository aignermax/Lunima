using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Routing.InterconnectRouting;

namespace CAP.Avalonia.Services;

/// <summary>
/// The export profile of a design built entirely from the SiEPIC EBeam PDK: the
/// uniform optical waveguide width/GDS layer the PDK's process cross-section
/// (siepic-ebeam-pdk.json: strip, 0.5 µm on WG 1/0) stamps onto every optical pin
/// when a component is placed. When the profile resolves, the nazca export routes
/// waveguide interconnect on THAT layer/width instead of the legacy global defaults —
/// nazca's default interconnect layer 1111/0 does not exist in EBeam.lyp — and drops
/// the demofab bb_body frame (1003/0, also not in EBeam.lyp) from parametric-straight
/// stubs. Mixed-process, demo, or unstamped designs resolve to null and keep the
/// historical export byte-identically.
/// </summary>
internal sealed class SiepicEBeamExportProfile
{
    private SiepicEBeamExportProfile(double widthMicrometers, int gdsLayer)
    {
        WidthMicrometers = widthMicrometers;
        GdsLayer = gdsLayer;
    }

    /// <summary>Waveguide width in micrometers of the design's single EBeam cross-section.</summary>
    public double WidthMicrometers { get; }

    /// <summary>GDS layer of the design's single EBeam cross-section (Si core: 1/0).</summary>
    public int GdsLayer { get; }

    /// <summary>
    /// Resolves the profile for a canvas whose non-analysis components (groups
    /// flattened) ALL come from a SiEPIC module and whose optical pins ALL carry the
    /// same process width/layer stamps; returns null otherwise — a mixed/demo design
    /// or components without process stamps both keep the legacy export.
    /// </summary>
    public static SiepicEBeamExportProfile? Resolve(DesignCanvasViewModel canvas)
    {
        var components = canvas.Components
            .SelectMany(vm => Flatten(vm.Component))
            .Where(c => !c.IsAnalysisTool)
            .ToList();
        if (components.Count == 0 || components.Any(c => !IsSiepic(c)))
            return null;

        var opticalPins = components
            .SelectMany(c => c.PhysicalPins)
            .Where(p => p.MatterType == MatterType.Light)
            .ToList();
        if (opticalPins.Count == 0
            || opticalPins.Any(p => p.WaveguideWidthMicrometers == null || p.Layer == null))
            return null;

        var width = opticalPins[0].WaveguideWidthMicrometers!.Value;
        var layer = opticalPins[0].Layer!.Value;
        if (opticalPins.Any(p => p.WaveguideWidthMicrometers != width || p.Layer != layer))
            return null;

        return new SiepicEBeamExportProfile(width, layer);
    }

    /// <summary>Returns a copy of <paramref name="settings"/> routed on the EBeam cross-section.</summary>
    public InterconnectSettings ApplyTo(InterconnectSettings settings)
    {
        var resolved = settings.Clone();
        resolved.WidthMicrometers = WidthMicrometers;
        resolved.GdsLayer = GdsLayer;
        return resolved;
    }

    private static bool IsSiepic(Component component) =>
        component.NazcaModuleName?.StartsWith("siepic", StringComparison.OrdinalIgnoreCase) == true;

    private static IEnumerable<Component> Flatten(Component component) =>
        component is ComponentGroup group
            ? group.GetAllComponentsRecursive()
            : new[] { component };
}
