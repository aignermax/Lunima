using System.Collections.Immutable;
using System.Diagnostics;
using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Components.Connections;

public partial class WaveguideConnectionManager
{
    private readonly ContentionRepairService _contentionRepair;

    internal ImmutableArray<ContentionRepairService.Failure> CaptureContentionFailures() =>
        _contentionRepair.CaptureFailures();

    internal void RestoreContentionFailures(IEnumerable<ContentionRepairService.Failure> failures) =>
        _contentionRepair.RestoreFailures(failures);

    /// <summary>Hard wall-clock budget for the targeted contention-repair pass.</summary>
    internal TimeSpan ContentionRepairTimeBudget
    {
        get => _contentionRepair.TimeBudget;
        set => _contentionRepair.TimeBudget = value;
    }

    /// <summary>Number of attempts made by the last contention-repair pass.</summary>
    public int LastContentionRepairAttemptCount
    {
        get => _contentionRepair.AttemptCount;
        private set => _contentionRepair.AttemptCount = value;
    }

    /// <summary>Number of attempts accepted by the last contention-repair pass.</summary>
    public int LastContentionRepairAcceptCount
    {
        get => _contentionRepair.AcceptCount;
        private set => _contentionRepair.AcceptCount = value;
    }

    internal void RepairContentionBlockedWires(CancellationToken cancellationToken = default) =>
        _contentionRepair.Run(UseSequentialRouting, cancellationToken);

    internal bool TryRepairContentionWire(WaveguideConnection blocked, List<WaveguideConnection> siblings,
        PathfindingGrid grid, Stopwatch budget, CancellationToken cancellationToken) =>
        _contentionRepair.TryRepairWire(blocked, siblings, grid, budget, cancellationToken);

    private bool IsCrossingParticipant(WaveguideConnection connection) =>
        CrossingService?.IsSubConnection(connection) == true;
}
