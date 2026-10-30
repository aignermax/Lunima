using System.Numerics;
using System.Collections.Immutable;

namespace CAP_Core.LightCalculation.Snapshots;

/// <summary>A source field already resolved from caller-owned laser and injection configuration.</summary>
public readonly record struct SimulationSourceInput(Guid FlowId, int WavelengthNm, Complex Field);

/// <summary>Detached simulation input captured at the document mutation boundary.</summary>
public sealed class SimulationInputSnapshot
{
    internal SimulationInputSnapshot(ImmutableArray<Guid> componentIds, ImmutableArray<Guid> connectionIds,
        ImmutableArray<ComponentMatrixSnapshot> components, ImmutableArray<SimulationSourceInput> sources,
        ImmutableDictionary<int, ImmutableArray<SimulationTransfer>> transfers, bool combineIncoherently)
    {
        ComponentIds = componentIds;
        ConnectionIds = connectionIds;
        Components = components;
        Sources = sources;
        Transfers = transfers;
        CombineIncoherently = combineIncoherently;
    }
    public ImmutableArray<Guid> ComponentIds { get; }
    public ImmutableArray<Guid> ConnectionIds { get; }
    internal ImmutableArray<ComponentMatrixSnapshot> Components { get; }
    internal ImmutableArray<SimulationSourceInput> Sources { get; }
    internal ImmutableDictionary<int, ImmutableArray<SimulationTransfer>> Transfers { get; }
    internal bool CombineIncoherently { get; }
}

/// <summary>Completed worker-owned fields and diagnostic matrix.</summary>
public sealed record SimulationSnapshotResult(IReadOnlyDictionary<Guid, Complex> Fields, SMatrix? SystemMatrix);

internal readonly record struct SimulationTransfer(Guid From, Guid To, Complex Value);
