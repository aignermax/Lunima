namespace CAP_Core.Solvers.Fdtd;

/// <summary>
/// Request for an FDTD S-matrix computation: an exported component GDS plus the
/// ports and simulation settings. Consumed by <see cref="IFdtdSMatrixService"/>
/// and serialised to the JSON contract of <c>scripts/fdtd_sparams.py</c>.
/// </summary>
public class FdtdSMatrixRequest
{
    /// <summary>
    /// Absolute host path to the component's exported GDS file. Optional when
    /// <see cref="Polygons"/> are supplied instead.
    /// </summary>
    public string GdsPath { get; init; } = string.Empty;

    /// <summary>
    /// Geometry polygons to build the component from, as an alternative to
    /// <see cref="GdsPath"/> (e.g. straight from the Nazca preview render).
    /// When non-empty, the solver builds from these and ignores the GDS path.
    /// </summary>
    public IReadOnlyList<FdtdPolygon> Polygons { get; init; } = Array.Empty<FdtdPolygon>();

    /// <summary>Ports to attach to the geometry before solving.</summary>
    public IReadOnlyList<FdtdPort> Ports { get; init; } = Array.Empty<FdtdPort>();

    /// <summary>Silicon layer/datatype the waveguide geometry sits on (default 1/0).</summary>
    public int LayerNumber { get; init; } = 1;

    /// <summary>Silicon datatype (default 0).</summary>
    public int LayerDatatype { get; init; }

    /// <summary>Sweep start wavelength in µm.</summary>
    public double WavelengthStart { get; init; } = 1.5;

    /// <summary>Sweep stop wavelength in µm.</summary>
    public double WavelengthStop { get; init; } = 1.6;

    /// <summary>Number of wavelength points in the sweep.</summary>
    public int WavelengthPoints { get; init; } = 11;

    /// <summary>Mesh resolution in pixels per µm. Higher = more accurate, slower.</summary>
    public int Resolution { get; init; } = 20;

    /// <summary>
    /// When true, runs a full 3D simulation (accurate, expensive). When false,
    /// a 2D approximation (fast, for quick checks). Default false.
    /// </summary>
    public bool Is3D { get; init; }

    /// <summary>Simulation padding in y in µm (must exceed the port mode width).</summary>
    public double YMargin { get; init; } = 2.0;

    /// <summary>Simulation padding in x in µm.</summary>
    public double XMargin { get; init; } = 2.0;

    /// <summary>
    /// Number of MPI ranks (CPU cores) to use. 0 lets the service choose a
    /// memory-safe value. Meep is CPU-only; there is no GPU acceleration.
    /// </summary>
    public int Cores { get; init; }
}
