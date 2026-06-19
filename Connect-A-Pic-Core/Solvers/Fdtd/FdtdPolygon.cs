namespace CAP_Core.Solvers.Fdtd;

/// <summary>
/// A geometry polygon on a GDS layer, used as an alternative to a GDS file when
/// feeding a single component to the FDTD solver. Lets the solver build the
/// component directly from the rendered geometry (e.g. from the Nazca preview)
/// without writing and mounting an intermediate GDS file.
/// </summary>
public class FdtdPolygon
{
    /// <summary>GDS layer number the polygon sits on.</summary>
    public int Layer { get; init; }

    /// <summary>Polygon vertices in µm.</summary>
    public IReadOnlyList<FdtdPoint> Points { get; init; } = Array.Empty<FdtdPoint>();
}

/// <summary>A 2D point in µm.</summary>
public readonly record struct FdtdPoint(double X, double Y);
