using System.Collections.Immutable;
using System.Numerics;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.Components.Core;
using CAP_Core.Grid;

namespace CAP_Core.LightCalculation.Snapshots;

/// <summary>Detached matrix definitions and compact connection physics shared across wavelength samples.</summary>
internal sealed class OpticalCircuitSnapshot
{
    private readonly ImmutableArray<ComponentMatrixSnapshot> _components;
    private readonly ImmutableArray<OpticalConnectionSnapshot> _connections;
    private readonly ImmutableArray<KeyValuePair<(Guid, Guid), Complex>> _tileTransfers;
    private readonly ImmutableArray<Guid> _sourceIds;

    private OpticalCircuitSnapshot(ImmutableArray<ComponentMatrixSnapshot> components,
        ImmutableArray<OpticalConnectionSnapshot> connections,
        ImmutableArray<KeyValuePair<(Guid, Guid), Complex>> tileTransfers, ImmutableArray<Guid> sourceIds)
    {
        _components = components;
        _connections = connections;
        _tileTransfers = tileTransfers;
        _sourceIds = sourceIds;
    }

    internal static OpticalCircuitSnapshot Capture(GridManager grid)
    {
        var components = ImmutableArray.CreateBuilder<ComponentMatrixSnapshot>();
        var connections = ImmutableArray.CreateBuilder<OpticalConnectionSnapshot>();
        var coherent = grid.WaveguideConnections.EnableCoherentPropagationPhase;
        if (grid.UsePhysicalCoordinates)
        {
            lock (grid.WaveguideConnections.SyncRoot)
                foreach (var wire in grid.WaveguideConnections.Connections)
                    if (OpticalConnectionSnapshot.Capture(wire, coherent) is { } value) connections.Add(value);
        }
        foreach (var component in grid.TileManager.GetAllComponents())
            Visit(component, components, connections, grid.UsePhysicalCoordinates, coherent);
        var tileTransfers = grid.UsePhysicalCoordinates ? ImmutableArray<KeyValuePair<(Guid, Guid), Complex>>.Empty
            : new SystemMatrixBuilder(grid).GetAllConnectionsBetweenComponents().ToImmutableArray();
        return new(components.ToImmutable(), connections.ToImmutable(), tileTransfers,
            grid.ExternalPortManager.GetUsedExternalInputs().Select(input => input.AttachedComponentPinId).ToImmutableArray());
    }

    /// <summary>Captures an isolated truth-table gate without constructing routing/grid/source owners.</summary>
    internal static OpticalCircuitSnapshot CaptureIsolatedGroup(ComponentGroup group, ImmutableArray<Guid> sourceIds)
    {
        var components = ImmutableArray.CreateBuilder<ComponentMatrixSnapshot>();
        var connections = ImmutableArray.CreateBuilder<OpticalConnectionSnapshot>();
        Visit(group, components, connections, physical: true, coherent: false);
        return new(components.ToImmutable(), connections.ToImmutable(),
            ImmutableArray<KeyValuePair<(Guid, Guid), Complex>>.Empty, sourceIds);
    }

    private static void Visit(Component component, ImmutableArray<ComponentMatrixSnapshot>.Builder components,
        ImmutableArray<OpticalConnectionSnapshot>.Builder connections, bool physical, bool coherent)
    {
        if (component is ComponentGroup group)
        {
            if (physical)
                foreach (var path in group.InternalPaths)
                    if (OpticalConnectionSnapshot.Capture(path, coherent) is { } value) connections.Add(value);
            foreach (var child in group.ChildComponents) Visit(child, components, connections, physical, coherent);
            return;
        }
        if (component.PhysicalPins.Count == 0) return;
        if (component.SimulationModelStatus == "geometry-only")
            throw new InvalidDataException($"Component '{component.Name}' is geometry-only; assign a simulation model first.");
        if (component.WaveLengthToSMatrixMap.Count == 0)
            throw new InvalidDataException($"No simulation matrix is defined for component {component.Identifier}.");
        components.Add(ComponentMatrixSnapshot.Capture(component));
    }

    /// <summary>Each run owns its mutable evaluation state, including formula delegates and slider maps.</summary>
    internal List<Dictionary<int, SMatrix>> RebuildComponents(CancellationToken cancellationToken)
    {
        var components = new List<Dictionary<int, SMatrix>>(_components.Length);
        foreach (var component in _components)
        {
            cancellationToken.ThrowIfCancellationRequested();
            components.Add(component.Rebuild());
        }
        return components;
    }

    internal SMatrix BuildMatrix(IReadOnlyList<Dictionary<int, SMatrix>> components, double wavelengthNm)
    {
        var matrices = components.Select(component => WavelengthInterpolator.GetMatrix(component, wavelengthNm, out _)).ToList();
        var transfers = _tileTransfers.ToDictionary(pair => pair.Key, pair => pair.Value);
        // Last-wins order matches the legacy builder: live routes followed by recursive frozen paths.
        foreach (var connection in _connections) connection.AddTransfers(transfers, wavelengthNm);
        var ids = transfers.Keys.SelectMany(pair => new[] { pair.Item1, pair.Item2 })
            .Concat(_sourceIds).Distinct().ToList();
        var wires = new SMatrix(ids, new());
        wires.SetValues(transfers);
        matrices.Add(wires);
        return SMatrix.CreateSystemSMatrix(matrices);
    }

    /// <summary>Hashes captured definitions and exact connection transfer at the requested sample, on the worker.</summary>
    internal void WriteIdentity(SnapshotPhysicsIdentity identity, double wavelengthNm)
    {
        identity.Write(_components.Length);
        foreach (var component in _components) identity.Component(component);
        var transfers = _tileTransfers.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var connection in _connections) connection.AddTransfers(transfers, wavelengthNm);
        identity.Transfers(transfers);
        identity.Write(_sourceIds.Length);
        foreach (var source in _sourceIds) identity.Id(source);
    }
}
