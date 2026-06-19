namespace CAP_Core.Solvers.Fdtd;

/// <summary>
/// A single optical port to attach to an imported GDS before FDTD.
/// Lunima knows its own pin positions, so it supplies these explicitly rather
/// than having the solver reconstruct them from the geometry.
/// </summary>
public class FdtdPort
{
    /// <summary>Port name (e.g. "o1"). Becomes part of the S-matrix keys.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Port centre x-coordinate in µm (GDS user units).</summary>
    public double X { get; init; }

    /// <summary>Port centre y-coordinate in µm (GDS user units).</summary>
    public double Y { get; init; }

    /// <summary>
    /// Outward-facing orientation in degrees (0 = +x/east, 90 = +y/north,
    /// 180 = -x/west, 270 = -y/south).
    /// </summary>
    public double Orientation { get; init; }

    /// <summary>Port (waveguide) width in µm.</summary>
    public double Width { get; init; }
}
