using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Analysis.LogicAnalysis;

/// <summary>
/// Derives a <see cref="LogicNetworkEvaluator"/> from the gate groups placed on the
/// canvas, making the canvas the source of truth for the wiring: a connection from
/// one group's external output pin to another group's external input pin becomes a
/// logic wire (fan-out of one output to several inputs is allowed). Unconnected
/// gate input pins become network-level inputs: pins carrying a persisted signal
/// name (issue #1025) merge into one network input per signal — the full adder's
/// thirteen addend-A pins become the single input <c>A</c> — while a pin without a
/// signal name keeps its own <c>&lt;group&gt;.&lt;pin&gt;</c> name and never merges
/// by bare pin name. Every gate output pin becomes a network-level output tap —
/// named by its persisted output signal name when it carries one (the adder's sum
/// reads <c>S</c>, its carry <c>Cout</c>), else <c>&lt;group&gt;.&lt;pin&gt;</c> —
/// also when it additionally drives another gate.
/// Bias pins take no part in wiring (they are constantly on — the extraction
/// contract); a connection into a bias pin, a connection between two input pins,
/// and a gate input driven by two different outputs are rejected with messages
/// naming the pins. A network input name shared with an output tap name is likewise
/// rejected — a name spanning both roles reads as the same wire in the Logic panel.
/// </summary>
public sealed partial class LogicNetworkBuilder
{
    private readonly GateDelayCalculator _delayCalculator = new();
    private readonly WireDelayCalculator _wireDelayCalculator = new();

    /// <summary>
    /// Derives and validates the logic network behind the given gate groups and the
    /// design's connections between their external pins.
    /// </summary>
    /// <param name="gates">
    /// The top-level gate groups, each with its logic model and role assignment.
    /// Gate ids come from the group names and must be unique.
    /// </param>
    /// <param name="connections">
    /// The design's waveguide connections. Only connections joining external pins of
    /// two gate groups take part in wiring — directly, or through passive pass-through
    /// optics (a waveguide, an edge coupler, the link between two chiplets), which the
    /// builder follows to the driving gate; connections towards anything else (a laser,
    /// an external port, an ungrouped component) are ignored — an unconnected gate
    /// input simply becomes a network-level input.
    /// </param>
    /// <param name="wavelengthNm">
    /// Wavelength in nm the per-gate propagation delays are derived at; defaults to
    /// the standard red wavelength when not provided.
    /// </param>
    /// <returns>The validated, evaluation-ready network.</returns>
    /// <exception cref="ArgumentException">
    /// A gate id is duplicated, a role assignment does not match its model or group,
    /// or a connection is logically invalid. The message names the offending pins.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The derived wiring forms a cycle that passes through no register-designated
    /// gate (<see cref="GateRoleAssignment.IsRegister"/>).
    /// </exception>
    public LogicNetworkEvaluator Build(
        IReadOnlyList<LogicGateInstance> gates,
        IReadOnlyList<WaveguideConnection> connections,
        double? wavelengthNm = null)
    {
        if (gates == null) throw new ArgumentNullException(nameof(gates));
        if (connections == null) throw new ArgumentNullException(nameof(connections));
        if (gates.Count == 0)
            throw new ArgumentException("A logic network needs at least one gate group.", nameof(gates));

        var contexts = gates.Select(GateContext.Create).ToList();
        var topology = CaptureTopology(contexts, connections, wavelengthNm ?? StandardWaveLengths.RedNM);
        return topology.Build(gates.ToDictionary(gate => gate.GateId ?? gate.Group.GroupName, gate => gate.Model));
    }

    internal LogicNetworkTopology CaptureTopology(IReadOnlyList<LogicGateTopologyInput> gates,
        IReadOnlyList<WaveguideConnection> connections, double wavelengthNm) =>
        CaptureTopology(gates.Select(GateContext.Create).ToList(), connections, wavelengthNm);

    private LogicNetworkTopology CaptureTopology(IReadOnlyList<GateContext> contexts,
        IReadOnlyList<WaveguideConnection> connections, double wavelengthNm)
    {
        ThrowOnDuplicateGateIds(contexts);

        var endpoints = new EndpointIndex(contexts);
        var adjacency = LogicPinTracer.BuildPinAdjacency(connections);
        var drivers = new Dictionary<LogicPinRef, LogicPinRef>();
        var edgeConnections = new Dictionary<LogicPinRef, IReadOnlyList<WaveguideConnection>>();
        foreach (var connection in connections)
        {
            AddConnectionDrivers(endpoints, adjacency, connection, drivers, edgeConnections);
        }

        return CaptureNetwork(contexts, drivers, edgeConnections, wavelengthNm);
    }

    /// <summary>
    /// Classifies one design connection and records the logic driver and wire it
    /// implies, if any. A connection whose far end is not a gate pin is followed
    /// through passive pass-through optics (waveguides, edge couplers, the link
    /// between two chiplets — any element with exactly one optical in→out path)
    /// until it reaches the driving gate pin, so a logic signal crossing a chiplet
    /// edge-coupler link keeps its driver. A gate input whose path cannot be
    /// resolved uniquely (branching, a dead end, or a non-pass-through element) is
    /// rejected with a diagnostic instead of silently degenerating into a network
    /// input; a gate output whose path dead-ends stays an output tap, as before.
    /// </summary>
    private static void AddConnectionDrivers(
        EndpointIndex endpoints,
        IReadOnlyDictionary<PhysicalPin, List<WaveguideConnection>> adjacency,
        WaveguideConnection connection,
        IDictionary<LogicPinRef, LogicPinRef> drivers,
        IDictionary<LogicPinRef, IReadOnlyList<WaveguideConnection>> edgeConnections)
    {
        var start = endpoints.Resolve(connection.StartPin);
        var end = endpoints.Resolve(connection.EndPin);
        if (start == null && end == null)
            return;

        var path = new List<WaveguideConnection> { connection };
        Endpoint first;
        Endpoint second;
        if (start != null && end != null)
        {
            first = start.Value;
            second = end.Value;
        }
        else
        {
            var resolved = (start ?? end)!.Value;
            var unresolvedPin = start == null ? connection.StartPin : connection.EndPin;
            if (unresolvedPin == null)
                return;
            var traced = TracePassThrough(endpoints, adjacency, unresolvedPin, connection, resolved, path, out var failure);
            if (traced == null)
            {
                if (failure != null && resolved.Role == PinRole.Input)
                    throw new ArgumentException(failure);
                return;
            }
            first = resolved;
            second = traced.Value;
        }

        var (source, load) = Classify(first, second);
        if (drivers.TryGetValue(load, out var existing) && !existing.Equals(source))
            throw new ArgumentException(
                $"Gate input '{Format(load)}' is driven by two different gate outputs: " +
                $"'{Format(existing)}' and '{Format(source)}'. One logic wire needs exactly one driver.");
        drivers[load] = source;
        edgeConnections.TryAdd(load, path);
    }

    /// <summary>
    /// Follows the optical path away from a gate pin through pass-through elements
    /// (see <see cref="LogicPinTracer"/>) until it reaches another gate pin. Returns
    /// null with a diagnostic <paramref name="failure"/> when the path loops, branches,
    /// or ends without reaching a gate pin.
    /// </summary>
    private static Endpoint? TracePassThrough(
        EndpointIndex endpoints,
        IReadOnlyDictionary<PhysicalPin, List<WaveguideConnection>> adjacency,
        PhysicalPin startPin,
        WaveguideConnection arrivalConnection,
        Endpoint gateEnd,
        List<WaveguideConnection> path,
        out string? failure)
    {
        var traced = LogicPinTracer.Trace(
            endpoints.Resolve, adjacency, startPin, arrivalConnection, path, out var reason);
        failure = reason == null ? null : UnresolvableMessage(gateEnd, startPin, reason);
        return traced;
    }

    /// <summary>Builds the diagnostic for a gate pin whose optical path resolves no unique driver.</summary>
    private static string UnresolvableMessage(Endpoint gateEnd, PhysicalPin startPin, string reason) =>
        $"Gate input '{Format(gateEnd.Pin)}' is wired towards '{LogicPinTracer.DescribePin(startPin)}', but the optical path {reason}. " +
        "The input would silently become a network input — connect it to exactly one gate output " +
        "through a single optical path, or remove the wire.";

    /// <summary>Determines which endpoint drives which, rejecting logically invalid pairings.</summary>
    private static (LogicPinRef Source, LogicPinRef Load) Classify(Endpoint first, Endpoint second)
    {
        if (first.Role == PinRole.Bias || second.Role == PinRole.Bias)
            throw new ArgumentException(
                $"Connection between '{Format(first.Pin)}' and '{Format(second.Pin)}' touches a bias pin. " +
                "Bias pins are constantly on and take no part in wiring — remove the connection.");
        if (first.Role == PinRole.Output && second.Role == PinRole.Input)
            return (first.Pin, second.Pin);
        if (second.Role == PinRole.Output && first.Role == PinRole.Input)
            return (second.Pin, first.Pin);
        if (first.Role == PinRole.Input)
            throw new ArgumentException(
                $"Connection joins two gate input pins: '{Format(first.Pin)}' and '{Format(second.Pin)}'. " +
                "A gate input must be driven by a gate output or left unconnected to become a network input.");
        throw new ArgumentException(
            $"Connection joins two gate output pins: '{Format(first.Pin)}' and '{Format(second.Pin)}'. " +
            "A gate output drives gate inputs; it cannot be driven itself.");
    }

    /// <summary>Renders a gate pin the way network inputs and taps are named: <c>group.pin</c>.</summary>
    private static string Format(LogicPinRef pin) => $"{pin.GateId}.{pin.PinName}";

    /// <summary>
    /// Maps one connection endpoint onto its gate pin and role, or null when no gate
    /// group is involved. The load path (and live canvas wiring) binds a wire endpoint
    /// to the internal component pin behind a group's external pin — those resolve
    /// through the external pin's name, so a loaded design assembles straight from
    /// its own connections.
    /// </summary>
    private sealed class EndpointIndex
    {
        private readonly Dictionary<Component, GateContext> _groups = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<PhysicalPin, (GateContext Context, string Name)> _pins = new(ReferenceEqualityComparer.Instance);

        public EndpointIndex(IEnumerable<GateContext> contexts)
        {
            foreach (var context in contexts)
            {
                _groups.TryAdd(context.Group, context);
                foreach (var external in context.Group.ExternalPins)
                    if (external.InternalPin is { } pin) _pins.TryAdd(pin, (context, external.Name));
            }
        }

        public Endpoint? Resolve(PhysicalPin? pin)
        {
            if (pin?.ParentComponent == null) return null;
            // Direct group ownership takes precedence; otherwise the first matching
            // external pin wins, including pins without a role. Preserve validation semantics.
            if (_groups.TryGetValue(pin.ParentComponent, out var context))
                return ToEndpoint(context, pin.Name);
            return _pins.TryGetValue(pin, out var external)
                ? ToEndpoint(external.Context, external.Name) : null;
        }
    }

    /// <summary>Wraps one gate pin name as an endpoint, or null when the pin carries no role.</summary>
    private static Endpoint? ToEndpoint(GateContext context, string pinName)
    {
        var role = context.RoleOf(pinName);
        return role == null ? null : new Endpoint(new LogicPinRef(context.GateId, pinName), role.Value);
    }

    /// <summary>
    /// Assembles the evaluator: unconnected gate inputs become network-level inputs
    /// (one per signal name, see <see cref="NetworkInputName"/>), and every gate
    /// output pin becomes a network-level output tap named
    /// <c>&lt;group&gt;.&lt;pin&gt;</c>. Each gate's propagation delay is derived
    /// from its group's internal optical path length, and each inter-gate wire's
    /// delay from the connecting waveguide's routed path.
    /// </summary>
    private LogicNetworkTopology CaptureNetwork(
        IReadOnlyList<GateContext> contexts,
        IReadOnlyDictionary<LogicPinRef, LogicPinRef> drivers,
        IReadOnlyDictionary<LogicPinRef, IReadOnlyList<WaveguideConnection>> edgeConnections,
        double wavelengthNm)
    {
        var networkInputs = new List<string>();
        var inputMembers = new Dictionary<string, List<string>>();
        var wiring = new Dictionary<LogicPinRef, LogicNetDriver>();
        var outputTaps = new Dictionary<string, LogicPinRef>();
        var delays = new Dictionary<string, double>();
        var wireDelays = new Dictionary<LogicWireEdge, double>();
        var wireLinkLosses = new Dictionary<LogicWireEdge, LogicWireLinkLoss>();

        foreach (var (load, path) in edgeConnections)
        {
            var edge = new LogicWireEdge(drivers[load], load);
            wireDelays[edge] = path.Sum(
                segment => _wireDelayCalculator.CalculatePicoseconds(segment, wavelengthNm));
            var linkLoss = WireLinkLossCalculator.ForPath(path, wavelengthNm);
            if (linkLoss != null)
                wireLinkLosses[edge] = linkLoss;
        }

        foreach (var context in contexts)
        {
            delays[context.GateId] = _delayCalculator.CalculatePicoseconds(context.Group, wavelengthNm);
            foreach (var pinName in context.InputPinNames)
            {
                var load = new LogicPinRef(context.GateId, pinName);
                wiring[load] = drivers.TryGetValue(load, out var source)
                    ? new LogicNetDriver.GateOutput(source)
                    : new LogicNetDriver.NetworkInput(NetworkInputName(networkInputs, inputMembers, context, load));
            }
            foreach (var pinName in context.OutputPinNames)
            {
                AddOutputTap(outputTaps, context, pinName);
            }
        }

        ThrowOnInputOutputNameCollision(inputMembers, outputTaps);

        var registerGateIds = contexts
            .Where(context => context.Roles.IsRegister)
            .Select(context => context.GateId)
            .ToList();
        return new LogicNetworkTopology(
            networkInputs, wiring, outputTaps, delays, wireDelays, registerGateIds, wireLinkLosses);
    }

    /// <summary>
    /// Registers one gate output pin as a network-level output tap: the pin's
    /// persisted signal name when it carries one — the adder's carry-out reads
    /// <c>Cout</c>, not <c>OROUT.Y</c> — else the raw <c>&lt;group&gt;.&lt;pin&gt;</c>
    /// name. Output names never merge (every tap is one gate output), so two pins
    /// landing on one tap name are rejected with a message naming both.
    /// </summary>
    private static void AddOutputTap(
        IDictionary<string, LogicPinRef> outputTaps, GateContext context, string pinName)
    {
        var tap = new LogicPinRef(context.GateId, pinName);
        var tapName = context.OutputSignalNameOf(pinName) ?? Format(tap);
        if (outputTaps.TryGetValue(tapName, out var existing) && !existing.Equals(tap))
            throw new ArgumentException(
                $"Two gate outputs are named '{tapName}': '{Format(existing)}' and '{Format(tap)}'. " +
                "Output signal names must be unique across the network — rename one of them.");
        outputTaps[tapName] = tap;
    }

    /// <summary>
    /// Registers and returns the network-level input name of an unconnected gate
    /// input: the pin's persisted signal name when it carries one — every member pin
    /// of the signal maps to the same network input, registered once — else the
    /// pin's own <c>&lt;group&gt;.&lt;pin&gt;</c> name (no merging by bare pin name).
    /// The pin is recorded as a member of its network input so a later name
    /// collision can name the pins behind the colliding name.
    /// </summary>
    private static string NetworkInputName(
        ICollection<string> networkInputs,
        IDictionary<string, List<string>> inputMembers,
        GateContext context,
        LogicPinRef load)
    {
        var name = context.SignalNameOf(load.PinName) ?? Format(load);
        if (!networkInputs.Contains(name))
            networkInputs.Add(name);
        if (!inputMembers.TryGetValue(name, out var members))
            inputMembers[name] = members = new List<string>();
        members.Add(Format(load));
        return name;
    }
}
