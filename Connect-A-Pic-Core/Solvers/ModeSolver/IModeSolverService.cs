namespace CAP_Core.Solvers.ModeSolver;

/// <summary>
/// Computes guided-mode properties (n_eff, n_g, polarisation, mode-field image)
/// for a given waveguide cross-section by delegating to a Python subprocess.
/// </summary>
public interface IModeSolverService
{
    /// <summary>
    /// Runs the mode solver for the specified cross-section and wavelength sweep.
    /// </summary>
    /// <param name="request">Cross-section geometry and wavelength list.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A result containing one <see cref="ModeSolverModeEntry"/> per (wavelength, mode) pair,
    /// or a failure result with a human-readable error and raw stderr.
    /// </returns>
    Task<ModeSolverResult> SolveAsync(ModeSolverRequest request, CancellationToken ct = default);
}
