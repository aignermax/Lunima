using System.Collections.Immutable;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.LightCalculation.Snapshots;

/// <summary>Captures document physics while its owner holds the coherent mutation boundary.</summary>
public static class SimulationSnapshotCapture
{
    public static SimulationInputSnapshot Capture(IEnumerable<Component> components,
        IEnumerable<WaveguideConnection> connections, IEnumerable<SimulationSourceInput> sources,
        bool coherentPropagationPhase, bool combineIncoherently = false)
    {
        var sourceValues = sources.ToImmutableArray();
        var componentIds = ImmutableArray.CreateBuilder<Guid>();
        var matrices = ImmutableArray.CreateBuilder<ComponentMatrixSnapshot>();
        var frozenPaths = new List<FrozenWaveguidePath>();
        foreach (var component in components) Visit(component, componentIds, matrices, frozenPaths);
        var wires = connections.ToArray();
        var connectionIds = wires.Select(wire => wire.Id).Concat(frozenPaths.Select(path => path.PathId)).ToImmutableArray();
        var transfers = SimulationTransferCapture.Capture(wires, frozenPaths,
            sourceValues.Select(source => source.WavelengthNm).Distinct(), coherentPropagationPhase);
        return new SimulationInputSnapshot(componentIds.ToImmutable(), connectionIds,
            matrices.ToImmutable(), sourceValues, transfers, combineIncoherently);
    }

    private static void Visit(Component component, ImmutableArray<Guid>.Builder ids,
        ImmutableArray<ComponentMatrixSnapshot>.Builder matrices, List<FrozenWaveguidePath> paths)
    {
        ids.Add(component.Id);
        if (component is ComponentGroup group)
        {
            // Groups are transparent in SystemMatrixBuilder: use leaf matrices and explicit internal
            // paths once, without computing a live transitive matrix or replacing the group's pins.
            paths.AddRange(group.InternalPaths);
            foreach (var child in group.ChildComponents) Visit(child, ids, matrices, paths);
            return;
        }
        if (component.PhysicalPins.Count == 0) return;
        if (component.SimulationModelStatus == "geometry-only")
            throw new InvalidDataException($"Component '{component.Name}' is geometry-only; assign a simulation model first.");
        if (component.WaveLengthToSMatrixMap.Count == 0)
            throw new InvalidDataException($"No simulation matrix is defined for component {component.Identifier}.");
        matrices.Add(ComponentMatrixSnapshot.Capture(component));
    }
}
