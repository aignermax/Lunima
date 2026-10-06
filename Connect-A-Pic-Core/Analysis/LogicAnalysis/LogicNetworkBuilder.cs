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
        ThrowOnDuplicateGateIds(contexts);

        var adjacency = BuildPinAdjacency(connections);
        var drivers = new Dictionary<LogicPinRef, LogicPinRef>();
        var edgeConnections = new Dictionary<LogicPinRef, IReadOnlyList<WaveguideConnection>>();
        foreach (var connection in connections)
        {
            AddConnectionDrivers(contexts, adjacency, connection, drivers, edgeConnections);
        }

        return AssembleNetwork(contexts, drivers, edgeConnections, wavelengthNm ?? StandardWaveLengths.RedNM);
    }

    /// <summary>Indexes every connection by both endpoint pins, so a trace can hop pin to pin.</summary>
    private static Dictionary<PhysicalPin, List<WaveguideConnection>> BuildPinAdjacency(
        IEnumerable<WaveguideConnection> connections)
    {
        var adjacency = new Dictionary<PhysicalPin, List<WaveguideConnection>>(
            ReferenceEqualityComparer.Instance);
        foreach (var connection in connections)
        {
            AddAdjacency(adjacency, connection.StartPin, connection);
            AddAdjacency(adjacency, connection.EndPin, connection);
        }
        return adjacency;
    }

    private static void AddAdjacency(
        IDictionary<PhysicalPin, List<WaveguideConnection>> adjacency,
        PhysicalPin? pin, WaveguideConnection connection)
    {
        if (pin == null)
            return;
        if (!adjacency.TryGetValue(pin, out var list))
            adjacency[pin] = list = new List<WaveguideConnection>();
        list.Add(connection);
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
        IReadOnlyList<GateContext> contexts,
        IReadOnlyDictionary<PhysicalPin, List<WaveguideConnection>> adjacency,
        WaveguideConnection connection,
        IDictionary<LogicPinRef, LogicPinRef> drivers,
        IDictionary<LogicPinRef, IReadOnlyList<WaveguideConnection>> edgeConnections)
    {
        var start = ResolveEndpoint(contexts, connection.StartPin);
        var end = ResolveEndpoint(contexts, connection.EndPin);
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
            var traced = TracePassThrough(contexts, adjacency, unresolvedPin, connection, resolved, path, out var failure);
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
    /// until it reaches another gate pin. Returns null with a diagnostic
    /// <paramref name="failure"/> when the path loops, branches, or ends without
    /// reaching a gate pin. The hopped connections are appended to
    /// <paramref name="path"/> so the wire delay covers every crossed segment.
    /// </summary>
    private static Endpoint? TracePassThrough(
        IReadOnlyList<GateContext> contexts,
        IReadOnlyDictionary<PhysicalPin, List<WaveguideConnection>> adjacency,
        PhysicalPin startPin,
        WaveguideConnection arrivalConnection,
        Endpoint gateEnd,
        List<WaveguideConnection> path,
        out string? failure)
    {
        failure = null;
        var visited = new HashSet<PhysicalPin>(ReferenceEqualityComparer.Instance);
        var pin = startPin;
        var arrival = arrivalConnection;
        while (true)
        {
            if (!visited.Add(pin))
            {
                failure = UnresolvableMessage(gateEnd, startPin,
                    $"loops back onto '{DescribePin(pin)}' without reaching a gate pin");
                return null;
            }
            var endpoint = ResolveEndpoint(contexts, pin);
            if (endpoint != null)
                return endpoint;
            var exit = PassThroughExit(pin);
            // A source sitting right at the gate pin (a laser or grating coupler feeding
            // the input) is not a logic wire: the input stays a network input, as before.
            if (exit == null && ReferenceEquals(pin, startPin))
                return null;
            if (exit == null)
            {
                failure = UnresolvableMessage(gateEnd, startPin,
                    $"ends at '{DescribePin(pin)}' — no gate pin is reachable through it");
                return null;
            }
            // The pass-through's far side can itself be a gate pin: the load path
            // binds a wire to either side of a two-pin component inside a gate
            // group, while the group exposes only one of them.
            var exitEndpoint = ResolveEndpoint(contexts, exit);
            if (exitEndpoint != null)
                return exitEndpoint;
            var continuations = adjacency.TryGetValue(exit, out var incident)
                ? incident.Where(c => !ReferenceEquals(c, arrival)).ToList()
                : new List<WaveguideConnection>();
            if (continuations.Count != 1)
            {
                failure = UnresolvableMessage(gateEnd, startPin, continuations.Count == 0
                    ? $"ends at '{DescribePin(exit)}' without reaching a gate output"
                    : $"branches at '{DescribePin(exit)}' ({continuations.Count} waveguides) — no unique driver");
                return null;
            }
            arrival = continuations[0];
            path.Add(arrival);
            pin = ReferenceEquals(arrival.StartPin, exit) ? arrival.EndPin : arrival.StartPin;
            if (pin == null)
            {
                failure = UnresolvableMessage(gateEnd, startPin,
                    $"ends at '{DescribePin(exit)}' without reaching a gate output");
                return null;
            }
        }
    }

    /// <summary>
    /// The far-side pin of a passive pass-through element: a non-group component
    /// with exactly two physical pins (a waveguide, an edge coupler) has exactly
    /// one optical in→out path. Anything else — a group shell, a splitter, a
    /// one-port laser — has no unique continuation and stops the trace.
    /// </summary>
    private static PhysicalPin? PassThroughExit(PhysicalPin pin)
    {
        var parent = pin.ParentComponent;
        if (parent == null || parent is ComponentGroup)
            return null;
        if (parent.PhysicalPins.Count != 2 || !parent.PhysicalPins.Contains(pin))
            return null;
        return parent.PhysicalPins.First(p => !ReferenceEquals(p, pin));
    }

    /// <summary>Builds the diagnostic for a gate pin whose optical path resolves no unique driver.</summary>
    private static string UnresolvableMessage(Endpoint gateEnd, PhysicalPin startPin, string reason) =>
        $"Gate input '{Format(gateEnd.Pin)}' is wired towards '{DescribePin(startPin)}', but the optical path {reason}. " +
        "The input would silently become a network input — connect it to exactly one gate output " +
        "through a single optical path, or remove the wire.";

    /// <summary>Renders a physical pin as <c>component.pin</c> for diagnostics.</summary>
    private static string DescribePin(PhysicalPin pin) =>
        $"{pin.ParentComponent?.Identifier ?? "?"}.{pin.Name}";

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
    private static Endpoint? ResolveEndpoint(IReadOnlyList<GateContext> contexts, PhysicalPin? pin)
    {
        if (pin?.ParentComponent == null)
            return null;
        var context = contexts.FirstOrDefault(c => ReferenceEquals(c.Group, pin.ParentComponent));
        if (context != null)
            return ToEndpoint(context, pin.Name);

        foreach (var candidate in contexts)
        {
            var external = candidate.Group.ExternalPins.FirstOrDefault(p => ReferenceEquals(p.InternalPin, pin));
            if (external != null)
                return ToEndpoint(candidate, external.Name);
        }
        return null;
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
    private LogicNetworkEvaluator AssembleNetwork(
        IReadOnlyList<GateContext> contexts,
        IReadOnlyDictionary<LogicPinRef, LogicPinRef> drivers,
        IReadOnlyDictionary<LogicPinRef, IReadOnlyList<WaveguideConnection>> edgeConnections,
        double wavelengthNm)
    {
        var networkInputs = new List<string>();
        var inputMembers = new Dictionary<string, List<string>>();
        var wiring = new Dictionary<LogicPinRef, LogicNetDriver>();
        var outputTaps = new Dictionary<string, LogicPinRef>();
        var models = new Dictionary<string, LogicGateModel>();
        var delays = new Dictionary<string, double>();
        var wireDelays = new Dictionary<LogicWireEdge, double>();

        foreach (var (load, path) in edgeConnections)
        {
            wireDelays[new LogicWireEdge(drivers[load], load)] = path.Sum(
                segment => _wireDelayCalculator.CalculatePicoseconds(segment, wavelengthNm));
        }

        foreach (var context in contexts)
        {
            models[context.GateId] = context.Model;
            delays[context.GateId] = _delayCalculator.CalculatePicoseconds(context.Group, wavelengthNm);
            foreach (var pinName in context.Model.InputPinNames)
            {
                var load = new LogicPinRef(context.GateId, pinName);
                wiring[load] = drivers.TryGetValue(load, out var source)
                    ? new LogicNetDriver.GateOutput(source)
                    : new LogicNetDriver.NetworkInput(NetworkInputName(networkInputs, inputMembers, context, load));
            }
            foreach (var pinName in context.Model.OutputPinNames)
            {
                AddOutputTap(outputTaps, context, pinName);
            }
        }

        ThrowOnInputOutputNameCollision(inputMembers, outputTaps);

        var registerGateIds = contexts
            .Where(context => context.Instance.Roles.IsRegister)
            .Select(context => context.GateId)
            .ToList();
        return new LogicNetworkEvaluator(
            networkInputs, models, wiring, outputTaps, delays, wireDelays, registerGateIds);
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
