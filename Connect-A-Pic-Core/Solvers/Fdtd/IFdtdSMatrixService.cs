namespace CAP_Core.Solvers.Fdtd;

/// <summary>
/// Recomputes a component's scattering matrix from its geometry using an
/// open-source FDTD solver (Meep), delegating to an external process.
/// Implementations are responsible for provisioning the solver environment
/// (e.g. a Docker image) on first use.
/// </summary>
public interface IFdtdSMatrixService
{
    /// <summary>
    /// Runs the FDTD solver for the component GDS and ports in
    /// <paramref name="request"/> and returns its S-matrix, or a failure result
    /// with a human-readable error and raw stderr.
    /// </summary>
    /// <param name="request">Component GDS, ports, and simulation settings.</param>
    /// <param name="progress">Optional sink for live solver progress lines.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<FdtdSMatrixResult> SolveAsync(
        FdtdSMatrixRequest request, IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Quickly probes whether the solver backend is ready (e.g. the Docker engine is
    /// installed and running), so the UI can warn before starting a long solve.
    /// </summary>
    Task<FdtdAvailability> CheckAvailabilityAsync(CancellationToken ct = default);
}
