namespace CAP_Core.Routing.WaveguideCompanions;

/// <summary>
/// One process-defined layer drawn along every routed waveguide core, e.g. a trench or
/// cladding-opening band on its own GDS layer. The band follows the core centerline and
/// is either a fixed offset wider on each side of the core (Nazca's <c>growx</c>) or a
/// fixed total width independent of the core width.
/// </summary>
public sealed record WaveguideCompanionLayer
{
    /// <summary>
    /// Creates a companion layer. Exactly one of <paramref name="offsetEachSideMicrometers"/>
    /// and <paramref name="totalWidthMicrometers"/> must be given.
    /// </summary>
    /// <param name="name">Display name (e.g. "ShallowTrench").</param>
    /// <param name="gdsLayer">GDS layer number the band is drawn on.</param>
    /// <param name="gdsDataType">GDS datatype the band is drawn on.</param>
    /// <param name="role">What the band means to the foundry (drives DRC).</param>
    /// <param name="offsetEachSideMicrometers">Growth beyond the core edge on each side, in µm.</param>
    /// <param name="totalWidthMicrometers">Absolute band width in µm (centered on the core).</param>
    /// <exception cref="ArgumentException">Neither or both widths given, or a negative/non-finite value.</exception>
    public WaveguideCompanionLayer(
        string name, int gdsLayer, int gdsDataType, WaveguideCompanionRole role,
        double? offsetEachSideMicrometers, double? totalWidthMicrometers)
    {
        if (offsetEachSideMicrometers.HasValue == totalWidthMicrometers.HasValue)
            throw new ArgumentException("Specify exactly one of offset-each-side or total width.");
        if (offsetEachSideMicrometers is { } offset && !(offset >= 0 && double.IsFinite(offset)))
            throw new ArgumentException("Offset each side must be a finite value ≥ 0.", nameof(offsetEachSideMicrometers));
        if (totalWidthMicrometers is { } total && !(total > 0 && double.IsFinite(total)))
            throw new ArgumentException("Total width must be a finite value > 0.", nameof(totalWidthMicrometers));

        Name = name;
        GdsLayer = gdsLayer;
        GdsDataType = gdsDataType;
        Role = role;
        OffsetEachSideMicrometers = offsetEachSideMicrometers;
        TotalWidthMicrometers = totalWidthMicrometers;
    }

    /// <summary>Display name of the band.</summary>
    public string Name { get; }

    /// <summary>GDS layer number.</summary>
    public int GdsLayer { get; }

    /// <summary>GDS datatype.</summary>
    public int GdsDataType { get; }

    /// <summary>Foundry meaning of the band.</summary>
    public WaveguideCompanionRole Role { get; }

    /// <summary>Growth beyond each core edge in µm, or null when an absolute width is declared.</summary>
    public double? OffsetEachSideMicrometers { get; }

    /// <summary>Absolute band width in µm, or null when an offset is declared.</summary>
    public double? TotalWidthMicrometers { get; }

    /// <summary>
    /// Growth beyond each core edge for a core of <paramref name="coreWidthMicrometers"/>
    /// (Nazca <c>growx</c>). An absolute width narrower than the core yields 0 — the band
    /// never shrinks below the core.
    /// </summary>
    public double OffsetFor(double coreWidthMicrometers) =>
        OffsetEachSideMicrometers
        ?? Math.Max(0.0, (TotalWidthMicrometers!.Value - coreWidthMicrometers) / 2.0);

    /// <summary>Total band width for a core of <paramref name="coreWidthMicrometers"/>.</summary>
    public double BandWidthFor(double coreWidthMicrometers) =>
        coreWidthMicrometers + 2.0 * OffsetFor(coreWidthMicrometers);
}
