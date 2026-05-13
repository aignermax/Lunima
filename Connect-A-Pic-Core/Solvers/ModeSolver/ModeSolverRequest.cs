namespace CAP_Core.Solvers.ModeSolver;

/// <summary>
/// Describes a waveguide cross-section and the wavelength sweep to pass to the
/// mode-solver bridge (<c>scripts/mode_solve.py</c>).
/// </summary>
public class ModeSolverRequest
{
    /// <summary>Waveguide core width in µm (e.g. 0.45 for a 450 nm SOI strip).</summary>
    public double Width { get; init; } = 0.45;

    /// <summary>Waveguide core height (thickness) in µm (e.g. 0.22 for SOI 220 nm).</summary>
    public double Height { get; init; } = 0.22;

    /// <summary>
    /// Slab height in µm for rib waveguides; use 0.0 for a fully-etched strip guide.
    /// </summary>
    public double SlabHeight { get; init; } = 0.0;

    /// <summary>Real part of the core material refractive index (e.g. 3.48 for silicon at 1550 nm).</summary>
    public double CoreIndex { get; init; } = 3.48;

    /// <summary>Real part of the cladding refractive index (e.g. 1.44 for SiO₂).</summary>
    public double CladIndex { get; init; } = 1.44;

    /// <summary>
    /// Wavelengths in µm at which to compute modes (e.g. [1.55] for C-band centre).
    /// </summary>
    public IReadOnlyList<double> Wavelengths { get; init; } = new[] { 1.55 };

    /// <summary>Python solver backend to use.</summary>
    public ModeSolverBackend Backend { get; init; } = ModeSolverBackend.GdsfactoryModes;

    /// <summary>Maximum number of modes to return per wavelength (default 4).</summary>
    public int NumModes { get; init; } = 4;
}
