using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

/// <summary>
/// Which GDS layers of one component play the waveguide-core and metal roles in its
/// preview (<see cref="GdsPreviewLayerStyle"/>). Derived from the component's fabrication
/// process: every layer an Optical cross-section references is a core, every layer a Metal
/// cross-section references is metal — so a CornerStone SiN core on layer 203 renders in
/// the waveguide colour instead of the faint "other" grey (issue #1494).
/// </summary>
/// <param name="CoreLayers">GDS layer numbers of waveguide cores (painted last, waveguide colour).</param>
/// <param name="MetalLayers">GDS layer numbers of metal / heater layers (copper colour).</param>
public sealed record GdsPreviewLayerRoles(IReadOnlySet<int> CoreLayers, IReadOnlySet<int> MetalLayers)
{
    /// <summary>
    /// The bundled demo PDK's layer roles: 1 = deep-etch / Si core, 3 = shallow-etch core,
    /// 10 = metal / heater. Used whenever a component's process cannot be resolved, keeping
    /// the pre-#1494 behaviour for the demo PDK and process-less components.
    /// </summary>
    public static GdsPreviewLayerRoles Default { get; } =
        new(new HashSet<int> { 1, 3 }, new HashSet<int> { 10 });

    /// <summary>
    /// Stable string identity of the role assignment. Included in the rasterised-bitmap
    /// cache key so the same geometry under two processes never shares a wrongly
    /// coloured bitmap.
    /// </summary>
    public string Signature =>
        "core:" + string.Join(',', CoreLayers.OrderBy(layer => layer))
        + "|metal:" + string.Join(',', MetalLayers.OrderBy(layer => layer));

    /// <summary>
    /// Derives the roles from a fabrication process: cross-sections name their layers and
    /// the process layer stack maps names to GDS layer numbers. Falls back to
    /// <see cref="Default"/> when the process is null or resolves no optical core layer
    /// (the demo PDK declares only a metal cross-section, so it keeps today's behaviour).
    /// </summary>
    public static GdsPreviewLayerRoles FromProcess(ProcessDefinition? process)
    {
        if (process == null)
            return Default;

        var layerNumberByName = BuildLayerNumberLookup(process);
        var cores = ResolveLayers(process, XsectionKind.Optical, layerNumberByName);
        if (cores.Count == 0)
            return Default;

        var metals = ResolveLayers(process, XsectionKind.Metal, layerNumberByName);
        return new GdsPreviewLayerRoles(cores, metals);
    }

    private static IReadOnlyDictionary<string, int> BuildLayerNumberLookup(ProcessDefinition process)
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in process.Layers)
            lookup[layer.Name] = layer.Layer;
        return lookup;
    }

    private static HashSet<int> ResolveLayers(
        ProcessDefinition process, XsectionKind kind, IReadOnlyDictionary<string, int> layerNumberByName) =>
        process.Xsections
            .Where(xsection => xsection.Kind == kind)
            .SelectMany(xsection => xsection.Layers)
            .Where(layerNumberByName.ContainsKey)
            .Select(name => layerNumberByName[name])
            .ToHashSet();
}
