using CAP_Core.Grid;

namespace CAP_Core.Analysis;

public interface IParameterSweepRunner
{
    Task<List<SweepDataPoint>> RunAsync(SweepConfiguration configuration, GridManager grid,
        IExternalPortManager ports, CancellationToken cancellationToken, IProgress<int>? progress = null);
}

/// <summary>Captures synchronously, then schedules only owned immutable circuit values.</summary>
public class ParameterSweepRunner : IParameterSweepRunner
{
    public Task<List<SweepDataPoint>> RunAsync(SweepConfiguration configuration, GridManager grid,
        IExternalPortManager ports, CancellationToken cancellationToken, IProgress<int>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = ParameterSweepSnapshot.Capture(configuration, grid, ports);
        return RunDetachedAsync(snapshot, cancellationToken, progress);
    }

    private Task<List<SweepDataPoint>> RunDetachedAsync(ParameterSweepSnapshot snapshot,
        CancellationToken token, IProgress<int>? progress) =>
        Task.Run(() => SolveSnapshotAsync(snapshot, token, progress), token);

    protected virtual Task<List<SweepDataPoint>> SolveSnapshotAsync(ParameterSweepSnapshot snapshot,
        CancellationToken cancellationToken, IProgress<int>? progress) => snapshot.SolveAsync(cancellationToken, progress);
}
