using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Analysis.LogicAnalysis.GateView;

/// <summary>
/// Reads the gate-level view of a design: the gates are exactly the groups the
/// <see cref="LogicNetworkAssembler"/> treats as gates (persisted pin assignment,
/// nested cell gates included), and the nets follow the same canvas connections and
/// pass-through optics the <see cref="LogicNetworkBuilder"/> wires. Unlike the
/// builder it never throws on a wiring the logic network would reject — a view of a
/// half-edited design must still draw — and it simulates nothing, so it can be
/// rebuilt on every design change.
/// </summary>
public sealed class GateViewGraphExtractor
{
    private enum Role { Input, Output, Bias }

    private readonly record struct GatePin(LogicPinRef Pin, Role Role);

    /// <summary>Extracts the gate view of a design.</summary>
    /// <param name="components">The design's top-level components.</param>
    /// <param name="connections">The design's waveguide connections.</param>
    /// <param name="cancellationToken">Cancels between connections.</param>
    public GateViewGraph Extract(
        IReadOnlyList<Component> components,
        IReadOnlyList<WaveguideConnection> connections,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(connections);

        var gates = new List<(ComponentGroup Group, string GateId)>();
        var internalWires = new List<WaveguideConnection>();
        LogicNetworkAssembler.CollectGates(components, ancestorPath: null, gates, internalWires);
        if (gates.Count == 0)
            return GateViewGraph.Empty;

        var nodes = gates
            .GroupBy(gate => gate.GateId)
            .Select(group => CreateNode(group.First().Group, group.Key))
            .ToList();
        var pinIndex = BuildPinIndex(nodes);
        var allConnections = connections.Concat(internalWires).ToList();
        var drivers = DeriveDrivers(allConnections, pinIndex, cancellationToken);
        return new GateViewGraph(nodes, BuildNets(nodes, drivers));
    }

    private static GateViewNode CreateNode(ComponentGroup group, string gateId)
    {
        var assignment = group.TruthTablePinAssignment!;
        var inputs = assignment.InputPinNames
            .Select(pin => new GateViewPin(pin, Lookup(assignment.InputSignalNames, pin)))
            .ToList();
        var outputs = assignment.OutputPinNames
            .Select(pin => new GateViewPin(pin, Lookup(assignment.OutputSignalNames, pin)))
            .ToList();
        return new GateViewNode(gateId, group, TopLevelOf(group), inputs, outputs, assignment.IsRegister);
    }

    private static string? Lookup(IReadOnlyDictionary<string, string>? names, string pin) =>
        names != null && names.TryGetValue(pin, out var name) && !string.IsNullOrWhiteSpace(name) ? name : null;

    private static ComponentGroup TopLevelOf(ComponentGroup group)
    {
        var top = group;
        while (top.ParentGroup is ComponentGroup parent)
            top = parent;
        return top;
    }

    /// <summary>
    /// Maps every physical pin that stands for a gate pin onto it: the group's own
    /// external pins and the internal component pins behind them (the load path binds
    /// wires to the internal pin).
    /// </summary>
    private static Dictionary<PhysicalPin, GatePin> BuildPinIndex(IEnumerable<GateViewNode> nodes)
    {
        var index = new Dictionary<PhysicalPin, GatePin>(ReferenceEqualityComparer.Instance);
        foreach (var node in nodes)
        {
            var assignment = node.Group.TruthTablePinAssignment!;
            foreach (var external in node.Group.ExternalPins)
            {
                var role = RoleOf(assignment, external.Name);
                if (role == null)
                    continue;
                var gatePin = new GatePin(new LogicPinRef(node.GateId, external.Name), role.Value);
                if (external.InternalPin != null)
                    index.TryAdd(external.InternalPin, gatePin);
            }
            foreach (var pin in node.Group.PhysicalPins)
            {
                var role = RoleOf(assignment, pin.Name);
                if (role != null)
                    index.TryAdd(pin, new GatePin(new LogicPinRef(node.GateId, pin.Name), role.Value));
            }
        }
        return index;
    }

    private static Role? RoleOf(TruthTablePinAssignment assignment, string pin)
    {
        if (assignment.InputPinNames.Contains(pin)) return Role.Input;
        if (assignment.OutputPinNames.Contains(pin)) return Role.Output;
        if (assignment.BiasPinNames.Contains(pin)) return Role.Bias;
        return null;
    }

    /// <summary>The driving gate output of every wired gate input; the first driver found wins.</summary>
    private static Dictionary<LogicPinRef, LogicPinRef> DeriveDrivers(
        IReadOnlyList<WaveguideConnection> connections,
        IReadOnlyDictionary<PhysicalPin, GatePin> pinIndex,
        CancellationToken cancellationToken)
    {
        var adjacency = LogicPinTracer.BuildPinAdjacency(connections);
        GatePin? Resolve(PhysicalPin pin) => pinIndex.TryGetValue(pin, out var gatePin) ? gatePin : null;
        var drivers = new Dictionary<LogicPinRef, LogicPinRef>();
        foreach (var connection in connections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pair = ResolvePair(connection, adjacency, Resolve);
            if (pair == null)
                continue;
            var (first, second) = pair.Value;
            if (first.Role == Role.Output && second.Role == Role.Input)
                drivers.TryAdd(second.Pin, first.Pin);
            else if (second.Role == Role.Output && first.Role == Role.Input)
                drivers.TryAdd(first.Pin, second.Pin);
        }
        return drivers;
    }

    private static (GatePin, GatePin)? ResolvePair(
        WaveguideConnection connection,
        IReadOnlyDictionary<PhysicalPin, List<WaveguideConnection>> adjacency,
        Func<PhysicalPin, GatePin?> resolve)
    {
        var start = connection.StartPin == null ? null : resolve(connection.StartPin);
        var end = connection.EndPin == null ? null : resolve(connection.EndPin);
        if (start != null && end != null)
            return (start.Value, end.Value);
        if (start == null && end == null)
            return null;
        var farPin = start == null ? connection.StartPin : connection.EndPin;
        if (farPin == null)
            return null;
        var traced = LogicPinTracer.Trace(resolve, adjacency, farPin, connection, new List<WaveguideConnection>(), out _);
        return traced == null ? null : ((start ?? end)!.Value, traced.Value);
    }

    /// <summary>One net per gate output pin, plus one per network input signal.</summary>
    private static List<GateViewNet> BuildNets(
        IReadOnlyList<GateViewNode> nodes, IReadOnlyDictionary<LogicPinRef, LogicPinRef> drivers)
    {
        var loadsByDriver = drivers
            .GroupBy(pair => pair.Value, pair => pair.Key)
            .ToDictionary(group => group.Key, group => group.ToList());
        var nets = new List<GateViewNet>();
        var inputNets = new Dictionary<string, (List<LogicPinRef> Loads, bool Named)>();
        foreach (var node in nodes)
        {
            foreach (var output in node.Outputs)
            {
                var source = new LogicPinRef(node.GateId, output.Name);
                var loads = loadsByDriver.TryGetValue(source, out var list) ? list : new List<LogicPinRef>();
                nets.Add(new GateViewNet(output.SignalName ?? Format(source), source, loads, output.SignalName != null));
            }
            foreach (var input in node.Inputs)
            {
                var load = new LogicPinRef(node.GateId, input.Name);
                if (drivers.ContainsKey(load))
                    continue;
                var name = input.SignalName ?? Format(load);
                if (!inputNets.TryGetValue(name, out var entry))
                    inputNets[name] = entry = (new List<LogicPinRef>(), input.SignalName != null);
                entry.Loads.Add(load);
            }
        }
        nets.AddRange(inputNets.Select(pair => new GateViewNet(pair.Key, null, pair.Value.Loads, pair.Value.Named)));
        return nets;
    }

    private static string Format(LogicPinRef pin) => $"{pin.GateId}.{pin.PinName}";
}
