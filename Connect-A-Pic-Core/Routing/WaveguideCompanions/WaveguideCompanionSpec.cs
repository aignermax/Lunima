namespace CAP_Core.Routing.WaveguideCompanions;

/// <summary>
/// The companion layers a fabrication process draws along every routed optical waveguide
/// — Lunima's counterpart of a Nazca xsection's non-core layers (core + layers with
/// <c>growx</c>). Empty for processes that draw the core only.
/// </summary>
public sealed class WaveguideCompanionSpec
{
    /// <summary>The spec of a process that draws only the waveguide core.</summary>
    public static WaveguideCompanionSpec Empty { get; } = new(Array.Empty<WaveguideCompanionLayer>());

    /// <summary>Creates a spec from its companion layers (in declaration order).</summary>
    public WaveguideCompanionSpec(IReadOnlyList<WaveguideCompanionLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        Layers = layers;
    }

    /// <summary>Companion layers in declaration order.</summary>
    public IReadOnlyList<WaveguideCompanionLayer> Layers { get; }

    /// <summary>True when the process declares no companion layer.</summary>
    public bool IsEmpty => Layers.Count == 0;

    /// <summary>
    /// The widest keep-out growth beyond each core edge for a core of
    /// <paramref name="coreWidthMicrometers"/>; 0 when no keep-out layer is declared.
    /// </summary>
    public double KeepOutOffsetFor(double coreWidthMicrometers)
    {
        double widest = 0.0;
        foreach (var layer in Layers)
        {
            if (layer.Role == WaveguideCompanionRole.KeepOut)
                widest = Math.Max(widest, layer.OffsetFor(coreWidthMicrometers));
        }
        return widest;
    }

    /// <summary>
    /// Union of two endpoint processes' specs for a connection between them: every layer of
    /// <paramref name="first"/>, plus each layer of <paramref name="second"/> on a GDS
    /// (layer, datatype) pair <paramref name="first"/> does not already draw.
    /// </summary>
    public static WaveguideCompanionSpec Union(WaveguideCompanionSpec first, WaveguideCompanionSpec second)
    {
        if (ReferenceEquals(first, second) || second.IsEmpty)
            return first;
        if (first.IsEmpty)
            return second;

        var layers = first.Layers.ToList();
        foreach (var candidate in second.Layers)
        {
            if (!layers.Any(l => l.GdsLayer == candidate.GdsLayer && l.GdsDataType == candidate.GdsDataType))
                layers.Add(candidate);
        }
        return new WaveguideCompanionSpec(layers);
    }
}
