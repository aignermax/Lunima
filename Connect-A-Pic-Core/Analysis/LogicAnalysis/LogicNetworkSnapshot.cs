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

    /// <summary>Captures compact definitions and scalar timing without computing any gate truth table.</summary>
    public static LogicNetworkSnapshot Capture(IReadOnlyList<Component> components,
        IReadOnlyList<WaveguideConnection> connections, int wavelength) =>
        Capture(components, connections, wavelength, new LogicNetworkBuilder());

    internal static LogicNetworkSnapshot Capture(IReadOnlyList<Component> components,
        IReadOnlyList<WaveguideConnection> connections, int wavelength, LogicNetworkBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(connections);
        var groups = new List<(ComponentGroup Group, string GateId)>();
        var internalWires = new List<WaveguideConnection>();
        LogicNetworkAssembler.CollectGates(components, null, groups, internalWires);
        if (groups.Count == 0)
            throw new InvalidOperationException(
                "The design contains no logic gate: no group carries a persisted " +
                "truth-table pin assignment. Extract a group's truth table in the Truth Table " +
                "panel to turn the group into a gate.");

        var gates = ImmutableArray.CreateBuilder<(string, TruthTableSnapshot)>(groups.Count);
        var topologyInputs = new List<LogicGateTopologyInput>(groups.Count);
        foreach (var (group, id) in groups)
        {
            var assignment = group.TruthTablePinAssignment!;
            var roles = new GateRoleAssignment(assignment.InputPinNames.ToImmutableArray(),
                assignment.OutputPinNames.ToImmutableArray(), assignment.BiasPinNames.ToImmutableArray(),
                assignment.Threshold, assignment.InputSignalNames?.ToImmutableDictionary(),
                assignment.OutputSignalNames?.ToImmutableDictionary(), assignment.IsRegister);
            gates.Add((id, TruthTableSnapshot.Capture(group, roles.InputPinNames, roles.OutputPinNames,
                roles.BiasPinNames, roles.PowerThreshold, wavelength)));
            topologyInputs.Add(new(group, roles, id));
        }
        var allConnections = connections.Concat(internalWires).ToList();
        return new(gates.ToImmutable(), builder.CaptureTopology(topologyInputs, allConnections, wavelength));
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
