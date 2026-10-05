namespace CAP_Core.Routing;

/// <summary>
/// Why a connection could not be routed cleanly. The classification decides whether
/// trying a different connection ordering can help (contention between wires) or is
/// provably pointless (a pin is sealed in by component geometry).
/// </summary>
public enum RoutingFailureReason
{
    /// <summary>No failure — the route is clean.</summary>
    None,

    /// <summary>
    /// The route is blocked by other routed waveguides (or the cause cannot be pinned
    /// on the endpoints). A different routing order may still succeed, so ordering
    /// retries are worthwhile.
    /// </summary>
    Contention,

    /// <summary>
    /// A start or end pin's escape corridor is sealed by a foreign component body:
    /// the pin cell or pin corridor is occupied by a component footprint. No wire
    /// ordering can free a footprint, so ordering retries cannot fix this wire.
    /// </summary>
    EndpointBlocked,
}
