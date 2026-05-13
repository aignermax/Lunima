namespace CAP_Core.Solvers.ModeSolver;

/// <summary>
/// Result returned by <see cref="IModeSolverService.SolveAsync"/>.
/// </summary>
public class ModeSolverResult
{
    /// <summary>True when the Python subprocess completed without error.</summary>
    public bool Success { get; init; }

    /// <summary>Human-readable error description when <see cref="Success"/> is false.</summary>
    public string? Error { get; init; }

    /// <summary>
    /// Raw stderr captured from the Python process, present on failure.
    /// Exposed directly so the UI can surface it without silent fallback.
    /// </summary>
    public string? RawStderr { get; init; }

    /// <summary>
    /// When non-null, identifies a Python package that must be installed to
    /// use the requested backend (e.g. "gdsfactory", "EMpy").
    /// The UI should show an "install …" hint rather than a generic error.
    /// </summary>
    public string? MissingBackend { get; init; }

    /// <summary>Name of the backend that was actually used (e.g. "GdsfactoryModes").</summary>
    public string? BackendUsed { get; init; }

    /// <summary>Computed modes, one entry per (wavelength, mode-index) pair.</summary>
    public IReadOnlyList<ModeSolverModeEntry> Modes { get; init; } = Array.Empty<ModeSolverModeEntry>();

    /// <summary>Creates a failure result with the given message.</summary>
    public static ModeSolverResult Fail(string error, string? rawStderr = null, string? missingBackend = null) =>
        new() { Success = false, Error = error, RawStderr = rawStderr, MissingBackend = missingBackend };
}
