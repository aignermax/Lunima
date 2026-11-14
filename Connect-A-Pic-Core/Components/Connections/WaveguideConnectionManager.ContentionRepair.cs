using System.Collections.Immutable;
using CAP_Core.Routing;
using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Components.Connections;

public partial class WaveguideConnectionManager
{
    private readonly ContentionRepairService _contentionRepair;

    internal ImmutableArray<ContentionRepairService.Failure> CaptureContentionFailures() =>
        _contentionRepair.CaptureFailures();

    internal void RestoreContentionFailures(IEnumerable<ContentionRepairService.Failure> failures) =>
        _contentionRepair.RestoreFailures(failures);

    /// <summary>
    /// Hard work budget for the targeted contention-repair pass, in A* node expansions shared by
    /// every route of every attempt (<see cref="ContentionRepairService.WorkBudget"/>).
    /// </summary>
    internal long ContentionRepairWorkBudget
    {
        get => _contentionRepair.WorkBudget;
        set => _contentionRepair.WorkBudget = value;
    }

    /// <summary>Node expansions the last contention-repair pass charged to its work budget.</summary>
    internal long LastContentionRepairNodesExpanded
    {
        get => _contentionRepair.NodesExpanded;
        private set => _contentionRepair.NodesExpanded = value;
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
        PathfindingGrid grid, RouteWorkBudget passBudget, CancellationToken cancellationToken) =>
        _contentionRepair.TryRepairWire(blocked, siblings, grid, passBudget, cancellationToken);

    private bool IsCrossingParticipant(WaveguideConnection connection) =>
        CrossingService?.IsSubConnection(connection) == true;
}
