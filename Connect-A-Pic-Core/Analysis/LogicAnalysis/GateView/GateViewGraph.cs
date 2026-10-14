using CAP_Core.Components.Core;

namespace CAP_Core.Analysis.LogicAnalysis.GateView;

/// <summary>One logic pin of a gate in the gate view, with its persisted signal name if any.</summary>
/// <param name="Name">The group's external pin name.</param>
/// <param name="SignalName">The persisted signal name (<c>A0</c>, <c>S</c>), or null.</param>
public sealed record GateViewPin(string Name, string? SignalName);

/// <summary>
/// One gate of the gate-level view: the gate group it abstracts, the top-level group a
/// click on it selects (the gate itself, or the cell instance it is nested in), and
/// its logic interface. <see cref="Kind"/> starts at the structural guess and is
/// refined once the gate's truth table is classified.
/// </summary>
public sealed class GateViewNode
{
    /// <summary>Creates a gate node.</summary>
    public GateViewNode(
        string gateId,
        ComponentGroup group,
        ComponentGroup topLevelGroup,
        IReadOnlyList<GateViewPin> inputs,
        IReadOnlyList<GateViewPin> outputs,
        bool isRegister)
    {
        GateId = gateId;
        Group = group;
        TopLevelGroup = topLevelGroup;
        Inputs = inputs;
        Outputs = outputs;
        IsRegister = isRegister;
        Kind = GateSymbolClassifier.FromStructure(inputs.Count, outputs.Count, isRegister);
    }

    /// <summary>The network gate id: the group name, or its hierarchical path for nested gates.</summary>
    public string GateId { get; }

    /// <summary>The physical gate group (position and size of the symbol).</summary>
    public ComponentGroup Group { get; }

    /// <summary>The top-level group selecting this gate selects.</summary>
    public ComponentGroup TopLevelGroup { get; }

    /// <summary>Logic input pins in truth-table bit order.</summary>
    public IReadOnlyList<GateViewPin> Inputs { get; }

    /// <summary>Logic output pins.</summary>
    public IReadOnlyList<GateViewPin> Outputs { get; }

    /// <summary>True for a register-designated gate.</summary>
    public bool IsRegister { get; }

    /// <summary>The schematic symbol; refined by <see cref="GateFunctionClassifier"/>.</summary>
    public GateSymbolKind Kind { get; set; }

    /// <summary>The short display name: the last segment of the gate id.</summary>
    public string DisplayName => GateId[(GateId.LastIndexOf('/') + 1)..];
}

/// <summary>
/// One signal of the gate view: driven by a gate output, or — when <see cref="Driver"/>
/// is null — a network input fed from outside (an unconnected gate input), named by
/// its signal name so all pins sharing that signal form one net.
/// </summary>
/// <param name="Name">Net label: the signal name, else <c>gate.pin</c>.</param>
/// <param name="Driver">The driving gate output pin, or null for a network input.</param>
/// <param name="Loads">The gate input pins the net feeds.</param>
/// <param name="IsNamed">True when the name is a persisted signal name, not a raw pin id.</param>
public sealed record GateViewNet(string Name, LogicPinRef? Driver, IReadOnlyList<LogicPinRef> Loads, bool IsNamed);

/// <summary>
/// The gate-level abstraction of a design: gates with their symbols and the nets
/// between their pins, read from the same groups and connections the logic network
/// is assembled from — but without simulating anything, so it is cheap to rebuild.
/// </summary>
public sealed class GateViewGraph
{
    /// <summary>Creates a graph.</summary>
    public GateViewGraph(IReadOnlyList<GateViewNode> nodes, IReadOnlyList<GateViewNet> nets)
    {
        Nodes = nodes;
        Nets = nets;
        NodesById = nodes.ToDictionary(node => node.GateId);
    }

    /// <summary>A graph without gates.</summary>
    public static GateViewGraph Empty { get; } = new([], []);

    /// <summary>Every gate, in design traversal order.</summary>
    public IReadOnlyList<GateViewNode> Nodes { get; }

    /// <summary>Every net: one per gate output, one per network input signal.</summary>
    public IReadOnlyList<GateViewNet> Nets { get; }

    /// <summary>Gates keyed by gate id.</summary>
    public IReadOnlyDictionary<string, GateViewNode> NodesById { get; }
}
