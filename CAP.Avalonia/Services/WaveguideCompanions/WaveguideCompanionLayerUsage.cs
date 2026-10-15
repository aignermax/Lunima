using CAP.Avalonia.Services.GdsImport.LayerVisibility;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Services.WaveguideCompanions;

/// <summary>
/// The GDS (layer, datatype) pairs the process companion bands of the canvas' routed
/// waveguides are drawn on, with how many waveguides carry each — so the layer panel can
/// show / hide / fade them exactly like imported layers.
/// </summary>
public static class WaveguideCompanionLayerUsage
{
    /// <summary>Companion-layer usage of <paramref name="canvas"/>, ordered by layer then datatype.</summary>
    public static IReadOnlyList<DesignLayerUsage> Collect(DesignCanvasViewModel canvas)
    {
        if (canvas.WaveguideCompanionSpecProvider == null)
            return Array.Empty<DesignLayerUsage>();

        var counts = new Dictionary<(int Layer, int DataType), int>();
        foreach (var path in WaveguideCompanionPathCollector.Collect(canvas))
        {
            foreach (var layer in canvas.GetWaveguideCompanions(path.StartPin, path.EndPin).Layers)
            {
                var key = (layer.GdsLayer, layer.GdsDataType);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        return counts
            .OrderBy(kv => kv.Key.Layer).ThenBy(kv => kv.Key.DataType)
            .Select(kv => new DesignLayerUsage(kv.Key.Layer, kv.Key.DataType, kv.Value))
            .ToList();
    }

    /// <summary>
    /// Merges imported-layer usage with companion-layer usage: shape counts of a pair used by
    /// both add up, the result stays ordered by layer then datatype.
    /// </summary>
    public static IReadOnlyList<DesignLayerUsage> Merge(
        IReadOnlyList<DesignLayerUsage> imported, IReadOnlyList<DesignLayerUsage> companions)
    {
        if (companions.Count == 0)
            return imported;

        return imported.Concat(companions)
            .GroupBy(u => (u.Layer, u.DataType))
            .OrderBy(g => g.Key.Layer).ThenBy(g => g.Key.DataType)
            .Select(g => new DesignLayerUsage(g.Key.Layer, g.Key.DataType, g.Sum(u => u.ShapeCount)))
            .ToList();
    }
}
