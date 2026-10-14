using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP_Core.Analysis.LogicAnalysis;

/// <summary>
/// Follows an optical path from a gate pin through passive pass-through optics (a
/// waveguide, an edge coupler, the link between two chiplets, a waveguide crossing)
/// until it reaches another gate pin. Shared by the <see cref="LogicNetworkBuilder"/>
/// (which turns an unresolvable gate input into a diagnostic) and the gate-level view
/// extraction (which simply drops it), so both read exactly the same wiring off the
/// canvas.
/// </summary>
internal static class LogicPinTracer
{
    /// <summary>Indexes every connection by both endpoint pins, so a trace can hop pin to pin.</summary>
    public static Dictionary<PhysicalPin, List<WaveguideConnection>> BuildPinAdjacency(
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
    /// Follows the optical path away from a gate pin through pass-through elements
    /// until <paramref name="resolve"/> recognises a gate pin. Returns null with a
    /// <paramref name="failureReason"/> when the path loops, branches, or ends without
    /// reaching a gate pin; a null result with a null reason means a source sits right
    /// at the starting pin (a laser feeding the input) — no logic wire, no error. The
    /// hopped connections are appended to <paramref name="path"/>.
    /// </summary>
    /// <typeparam name="TEndpoint">The caller's resolved gate-pin type.</typeparam>
    /// <param name="resolve">Maps a physical pin onto a gate pin, or null for any other pin.</param>
    /// <param name="adjacency">Connections indexed by endpoint pin (<see cref="BuildPinAdjacency"/>).</param>
    /// <param name="startPin">The connection end that is not a gate pin.</param>
    /// <param name="arrivalConnection">The connection the trace arrives through.</param>
    /// <param name="path">Receives every hopped connection.</param>
    /// <param name="failureReason">Why the trace failed, phrased to follow "the optical path …".</param>
    public static TEndpoint? Trace<TEndpoint>(
        Func<PhysicalPin, TEndpoint?> resolve,
        IReadOnlyDictionary<PhysicalPin, List<WaveguideConnection>> adjacency,
        PhysicalPin startPin,
        WaveguideConnection arrivalConnection,
        List<WaveguideConnection> path,
        out string? failureReason)
        where TEndpoint : struct
    {
        failureReason = null;
        var visited = new HashSet<PhysicalPin>(ReferenceEqualityComparer.Instance);
        var pin = startPin;
        var arrival = arrivalConnection;
        while (true)
        {
            if (!visited.Add(pin))
            {
                failureReason = $"loops back onto '{DescribePin(pin)}' without reaching a gate pin";
                return null;
            }
            var endpoint = resolve(pin);
            if (endpoint != null)
                return endpoint;
            var exit = PassThroughExit(pin);
            // A source sitting right at the gate pin (a laser or grating coupler feeding
            // the input) is not a logic wire: the input stays a network input.
            if (exit == null && ReferenceEquals(pin, startPin))
                return null;
            if (exit == null)
            {
                failureReason = $"ends at '{DescribePin(pin)}' — no gate pin is reachable through it";
                return null;
            }
            // The pass-through's far side can itself be a gate pin: the load path
            // binds a wire to either side of a two-pin component inside a gate
            // group, while the group exposes only one of them.
            var exitEndpoint = resolve(exit);
            if (exitEndpoint != null)
                return exitEndpoint;
            var continuations = adjacency.TryGetValue(exit, out var incident)
                ? incident.Where(c => !ReferenceEquals(c, arrival)).ToList()
                : new List<WaveguideConnection>();
            if (continuations.Count != 1)
            {
                failureReason = continuations.Count == 0
                    ? $"ends at '{DescribePin(exit)}' without reaching a gate output"
                    : $"branches at '{DescribePin(exit)}' ({continuations.Count} waveguides) — no unique driver";
                return null;
            }
            arrival = continuations[0];
            path.Add(arrival);
            var next = ReferenceEquals(arrival.StartPin, exit) ? arrival.EndPin : arrival.StartPin;
            if (next == null)
            {
                failureReason = $"ends at '{DescribePin(exit)}' without reaching a gate output";
                return null;
            }
            pin = next;
        }
    }

    /// <summary>
    /// The far-side pin of a passive pass-through element: a non-group component
    /// with exactly two physical pins (a waveguide, an edge coupler) has exactly
    /// one optical in→out path, and a waveguide crossing passes light straight on
    /// to its opposite port. Anything else — a group shell, a splitter, a one-port
    /// laser — has no unique continuation and stops the trace.
    /// </summary>
    public static PhysicalPin? PassThroughExit(PhysicalPin pin)
    {
        var parent = pin.ParentComponent;
        if (parent == null || parent is ComponentGroup)
            return null;
        if (CrossingComponentCatalog.IsCrossing(parent))
            return CrossingComponentCatalog.StraightThroughExit(pin);
        if (parent.PhysicalPins.Count != 2 || !parent.PhysicalPins.Contains(pin))
            return null;
        return parent.PhysicalPins.First(p => !ReferenceEquals(p, pin));
    }

    /// <summary>Renders a physical pin as <c>component.pin</c> for diagnostics.</summary>
    public static string DescribePin(PhysicalPin pin) =>
        $"{pin.ParentComponent?.Identifier ?? "?"}.{pin.Name}";
}
