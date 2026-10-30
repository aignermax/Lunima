using CAP_Core.Grid;

namespace CAP_Core.Analysis.OnaAnalysis;

/// <summary>Executes a prepared wavelength sweep until its actual solver cleanup completes.</summary>
public interface IWavelengthSweepRunner
{
    Task<WavelengthSweepResult> RunAsync(WavelengthSweepConfiguration configuration,
        GridManager grid, IExternalPortManager ports, CancellationToken cancellationToken);
}

/// <summary>Captures caller-owned physics, then runs the sweep on detached worker input.</summary>
public class WavelengthSweepRunner : IWavelengthSweepRunner
{
    public Task<WavelengthSweepResult> RunAsync(WavelengthSweepConfiguration configuration,
        GridManager grid, IExternalPortManager ports, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = WavelengthSweepSnapshot.Capture(configuration, grid, ports);
        return RunDetachedAsync(snapshot, cancellationToken);
    }

    // Keep closure creation away from method parameters containing live editor objects.
    private Task<WavelengthSweepResult> RunDetachedAsync(WavelengthSweepSnapshot snapshot, CancellationToken token) =>
        Task.Run(() => SolveSnapshotAsync(snapshot, token), token);

    /// <summary>Executes prepared physics without taking ownership of the document.</summary>
    protected virtual Task<WavelengthSweepResult> SolveSnapshotAsync(
        WavelengthSweepSnapshot snapshot, CancellationToken cancellationToken) => snapshot.SolveAsync(cancellationToken);
}
