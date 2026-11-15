using System.Collections.Immutable;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Analysis.LogicAnalysis;

/// <summary>All gate physics, interfaces and inter-gate topology captured before the first extraction awaits.</summary>
public sealed class LogicNetworkSnapshot
{
    private readonly ImmutableArray<(string Id, TruthTableSnapshot Physics)> _gates;
    private readonly LogicNetworkTopology _topology;

    private LogicNetworkSnapshot(ImmutableArray<(string, TruthTableSnapshot)> gates, LogicNetworkTopology topology)
    {
        _gates = gates;
        _topology = topology;
    }

    /// <summary>
    /// Gates captured per owner-thread slice by <see cref="CaptureAsync"/>: small enough that a
    /// slice stays far below the UI budget on slow machines, large enough that a design of a few
    /// hundred gates yields only about ten times.
    /// </summary>
    internal const int GatesPerSlice = 32;

    /// <summary>Captures compact definitions and scalar timing without computing any gate truth table.</summary>
    public static LogicNetworkSnapshot Capture(IReadOnlyList<Component> components,
        IReadOnlyList<WaveguideConnection> connections, int wavelength)
    {
        var gates = CollectGateGroups(components, connections, out var internalWires);
        var captured = new CapturedGates(gates.Count);
        foreach (var (group, id) in gates)
            captured.Add(group, id, wavelength);
        return captured.ToSnapshot(new LogicNetworkBuilder(), connections, internalWires, wavelength);
    }

    /// <summary>
    /// Same capture, cut into owner-thread slices of <see cref="GatesPerSlice"/> gates with
    /// <paramref name="slices"/> yielding between them and once before the topology. Every slice
    /// reads live state on the owner thread; <paramref name="slices"/> rejects a resumption after
    /// the design changed, so the parts of one snapshot always describe the same design.
    /// </summary>
    internal static async Task<LogicNetworkSnapshot> CaptureAsync(IReadOnlyList<Component> components,
        IReadOnlyList<WaveguideConnection> connections, int wavelength, LogicNetworkBuilder builder,
        ICaptureYield slices, CancellationToken cancellationToken)
    {
        var gates = CollectGateGroups(components, connections, out var internalWires);
        var captured = new CapturedGates(gates.Count);
        for (var index = 0; index < gates.Count; index++)
        {
            // No ConfigureAwait(false): the next slice must read live state on the owner thread.
            if (index > 0 && index % GatesPerSlice == 0)
                await slices.YieldAsync(cancellationToken);
            captured.Add(gates[index].Group, gates[index].GateId, wavelength);
        }
        await slices.YieldAsync(cancellationToken);
        return captured.ToSnapshot(builder, connections, internalWires, wavelength);
    }

    private static List<(ComponentGroup Group, string GateId)> CollectGateGroups(IReadOnlyList<Component> components,
        IReadOnlyList<WaveguideConnection> connections, out List<WaveguideConnection> internalWires)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(connections);
        var groups = new List<(ComponentGroup Group, string GateId)>();
        internalWires = new List<WaveguideConnection>();
        LogicNetworkAssembler.CollectGates(components, null, groups, internalWires);
        if (groups.Count == 0)
            throw new InvalidOperationException(
                "The design contains no logic gate: no group carries a persisted " +
                "truth-table pin assignment. Extract a group's truth table in the Truth Table " +
                "panel to turn the group into a gate.");
        return groups;
    }

    /// <summary>Gate physics and topology inputs accumulated in design order.</summary>
    private sealed class CapturedGates(int capacity)
    {
        private readonly ImmutableArray<(string, TruthTableSnapshot)>.Builder _gates =
            ImmutableArray.CreateBuilder<(string, TruthTableSnapshot)>(capacity);
        private readonly List<LogicGateTopologyInput> _topologyInputs = new(capacity);

        internal void Add(ComponentGroup group, string id, int wavelength)
        {
            var assignment = group.TruthTablePinAssignment!;
            var roles = new GateRoleAssignment(assignment.InputPinNames.ToImmutableArray(),
                assignment.OutputPinNames.ToImmutableArray(), assignment.BiasPinNames.ToImmutableArray(),
                assignment.Threshold, assignment.InputSignalNames?.ToImmutableDictionary(),
                assignment.OutputSignalNames?.ToImmutableDictionary(), assignment.IsRegister);
            _gates.Add((id, TruthTableSnapshot.Capture(group, roles.InputPinNames, roles.OutputPinNames,
                roles.BiasPinNames, roles.PowerThreshold, wavelength)));
            _topologyInputs.Add(new(group, roles, id));
        }

        internal LogicNetworkSnapshot ToSnapshot(LogicNetworkBuilder builder,
            IReadOnlyList<WaveguideConnection> connections, List<WaveguideConnection> internalWires, int wavelength)
        {
            var allConnections = connections.Concat(internalWires).ToList();
            return new(_gates.ToImmutable(), builder.CaptureTopology(_topologyInputs, allConnections, wavelength));
        }
    }

    internal async Task<LogicNetworkEvaluator> SolveAsync(TruthTableExtractor extractor, CancellationToken cancellationToken)
    {
        var models = new Dictionary<string, LogicGateModel>(_gates.Length);
        foreach (var gate in _gates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var table = await extractor.ExtractCapturedAsync(gate.Physics, cancellationToken).ConfigureAwait(false);
            models.Add(gate.Id, LogicGateModel.FromTruthTable(table));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return _topology.Build(models);
    }
}
