using System.Numerics;

namespace CAP_Core.Solvers.Fdtd;

/// <summary>
/// One scattering-matrix element across the wavelength sweep, e.g. key
/// "o2@0,o1@0" with complex transmission from input o1 (mode 0) to output o2.
/// </summary>
public class FdtdSEntry
{
    /// <summary>S-parameter key in "&lt;out&gt;@&lt;mode&gt;,&lt;in&gt;@&lt;mode&gt;" form.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Complex value per wavelength, aligned with <see cref="FdtdSMatrixResult.Wavelengths"/>.</summary>
    public IReadOnlyList<Complex> Values { get; init; } = Array.Empty<Complex>();
}

/// <summary>
/// Result of an FDTD S-matrix computation. On failure, carries a human-readable
/// error plus the raw solver stderr — never a silently-guessed S-matrix.
/// </summary>
public class FdtdSMatrixResult
{
    /// <summary>True when the solver completed and produced an S-matrix.</summary>
    public bool Success { get; init; }

    /// <summary>Human-readable error when <see cref="Success"/> is false.</summary>
    public string? Error { get; init; }

    /// <summary>Raw stderr from the solver, surfaced on failure (no silent fallback).</summary>
    public string? RawStderr { get; init; }

    /// <summary>
    /// When non-null, names a missing dependency (e.g. "docker", "gdsfactory")
    /// so the UI can show an install/setup hint instead of a generic error.
    /// </summary>
    public string? MissingDependency { get; init; }

    /// <summary>Whether the run was 3D.</summary>
    public bool Is3D { get; init; }

    /// <summary>Port names in S-matrix index order (e.g. ["o1","o2"]).</summary>
    public IReadOnlyList<string> Ports { get; init; } = Array.Empty<string>();

    /// <summary>Wavelength grid in µm.</summary>
    public IReadOnlyList<double> Wavelengths { get; init; } = Array.Empty<double>();

    /// <summary>S-matrix elements.</summary>
    public IReadOnlyList<FdtdSEntry> Entries { get; init; } = Array.Empty<FdtdSEntry>();

    /// <summary>
    /// Per-input-port summed |S|² at the centre wavelength. A passive device
    /// should be ≤ 1; a value above 1 signals an unconverged (e.g. coarse-2D) run.
    /// </summary>
    public IReadOnlyDictionary<string, double> EnergySumPerInput { get; init; }
        = new Dictionary<string, double>();

    /// <summary>Creates a failure result.</summary>
    public static FdtdSMatrixResult Fail(string error, string? rawStderr = null, string? missingDependency = null) =>
        new() { Success = false, Error = error, RawStderr = rawStderr, MissingDependency = missingDependency };
}
