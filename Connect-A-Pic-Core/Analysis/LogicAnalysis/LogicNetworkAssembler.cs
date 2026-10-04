using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Analysis.LogicAnalysis;

/// <summary>
/// Assembles an evaluable <see cref="LogicNetworkEvaluator"/> straight from a loaded
/// design — no hand-built <see cref="LogicGateInstance"/>s: every group carrying a
/// persisted <see cref="TruthTablePinAssignment"/> is a logic gate, its
/// <see cref="LogicGateModel"/> is re-extracted with exactly the persisted pin roles
/// and threshold, and the design's own connections wire the gates (the canvas stays
/// the source of truth, see <see cref="LogicNetworkBuilder"/>). Groups without a
/// persisted assignment are not gates: the assembler descends into them, so a gate
/// nested inside a hierarchical cell instance joins the network exactly like a
/// top-level one, and each pin-bound frozen internal path of such a plain group
/// becomes a virtual connection — the intra-cell wiring of a hierarchical design is
/// frozen into the cell, never live on the canvas. A gate group's own internal paths
/// are its extracted behaviour, never network edges. A design with no gate group at
/// all is reported as a readable error instead of yielding an empty network.
/// Identical groups are extracted per instance — the extraction is the source of the
/// model, and caching would only ever save simulation runs, never change the result.
/// </summary>
public sealed class LogicNetworkAssembler
{
    private readonly TruthTableExtractor _extractor;
    private readonly LogicNetworkBuilder _builder;

    /// <summary>
    /// Creates an assembler over the pipeline stages; the parameterless defaults cover
    /// the production path, tests inject their own stages.
    /// </summary>
    /// <param name="extractor">Re-extracts each gate's truth table from its group.</param>
    /// <param name="builder">Derives and validates the network from gates and connections.</param>
    public LogicNetworkAssembler(TruthTableExtractor? extractor = null, LogicNetworkBuilder? builder = null)
    {
        _extractor = extractor ?? new TruthTableExtractor();
        _builder = builder ?? new LogicNetworkBuilder();
    }

    /// <summary>
    /// Builds the logic network of a loaded design: collects the gate groups (every
    /// group with a persisted <see cref="TruthTablePinAssignment"/>, nested ones
    /// included), re-extracts each gate's model with the persisted roles and
    /// threshold, and lets the <see cref="LogicNetworkBuilder"/> derive the wiring
    /// from <paramref name="connections"/> plus the frozen internal paths of the
    /// plain groups the gates nest in. Extraction and builder errors pass through
    /// unchanged — their messages name the offending pins.
    /// </summary>
    /// <param name="components">
    /// The design's top-level components. Non-group components and groups without a
    /// persisted assignment are no gates themselves, but a plain group's subtree and
    /// frozen internal paths take part.
    /// </param>
    /// <param name="connections">
    /// The design's waveguide connections. Only connections joining external pins of
    /// two gate groups take part in wiring; everything else is ignored.
    /// </param>
    /// <param name="wavelengthNm">Laser wavelength in nm the gate tables are re-extracted at.</param>
    /// <param name="cancellationToken">Cancels the assembly between combinations.</param>
    /// <returns>The validated, evaluation-ready network behind the design's gate groups.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="components"/> or <paramref name="connections"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">The design contains no gate group.</exception>
    public async Task<LogicNetworkEvaluator> AssembleAsync(
        IReadOnlyList<Component> components,
        IReadOnlyList<WaveguideConnection> connections,
        int wavelengthNm,
        CancellationToken cancellationToken = default)
    {
        if (components == null) throw new ArgumentNullException(nameof(components));
        if (connections == null) throw new ArgumentNullException(nameof(connections));

        var gateGroups = new List<ComponentGroup>();
        var internalWires = new List<WaveguideConnection>();
        CollectGates(components, gateGroups, internalWires);
        if (gateGroups.Count == 0)
        {
            throw new InvalidOperationException(
                "The design contains no logic gate: no group carries a persisted " +
                "truth-table pin assignment. Extract a group's truth table in the Truth Table " +
                "panel to turn the group into a gate.");
        }

        var gates = new List<LogicGateInstance>(gateGroups.Count);
        foreach (var group in gateGroups)
        {
            gates.Add(await ExtractGateAsync(group, wavelengthNm, cancellationToken));
        }

        var allConnections = internalWires.Count == 0
            ? connections
            : connections.Concat(internalWires).ToList();
        return _builder.Build(gates, allConnections, wavelengthNm);
    }

    /// <summary>
    /// Collects the gates of one design level, descending into every group without a
    /// persisted assignment: a hierarchical design nests its gates inside plain
    /// cell-instance groups, and a cell's frozen internal paths carry the intra-cell
    /// wiring physically, so each pin-bound path becomes a virtual connection — the
    /// builder resolves its endpoint pins through the gate groups' external pins
    /// exactly like a canvas connection. A group WITH a persisted assignment is a
    /// gate: its subtree and internal paths are its own extracted behaviour.
    /// </summary>
    private static void CollectGates(
        IEnumerable<Component> components,
        ICollection<ComponentGroup> gates,
        ICollection<WaveguideConnection> internalWires)
    {
        foreach (var group in components.OfType<ComponentGroup>())
        {
            if (group.TruthTablePinAssignment != null)
            {
                gates.Add(group);
                continue;
            }
            foreach (var path in group.InternalPaths)
            {
                if (path.StartPin != null && path.EndPin != null)
                {
                    internalWires.Add(ToVirtualConnection(path));
                }
            }
            CollectGates(group.ChildComponents, gates, internalWires);
        }
    }

    /// <summary>Wraps one frozen group-internal path as a connection between its endpoint pins.</summary>
    private static WaveguideConnection ToVirtualConnection(FrozenWaveguidePath path)
    {
        var connection = new WaveguideConnection { StartPin = path.StartPin!, EndPin = path.EndPin! };
        connection.RestoreCachedPath(path.Path);
        return connection;
    }

    /// <summary>Re-extracts one gate group's model with exactly its persisted roles and threshold.</summary>
    private async Task<LogicGateInstance> ExtractGateAsync(
        ComponentGroup group, int wavelengthNm, CancellationToken cancellationToken)
    {
        var persisted = group.TruthTablePinAssignment!;
        var roles = new GateRoleAssignment(
            persisted.InputPinNames,
            persisted.OutputPinNames,
            persisted.BiasPinNames,
            persisted.Threshold,
            persisted.InputSignalNames,
            persisted.OutputSignalNames,
            persisted.IsRegister);
        var table = await _extractor.ExtractAsync(
            group,
            roles.InputPinNames,
            roles.OutputPinNames,
            roles.BiasPinNames,
            roles.PowerThreshold,
            wavelengthNm,
            cancellationToken);
        return new LogicGateInstance(group, LogicGateModel.FromTruthTable(table), roles);
    }
}
