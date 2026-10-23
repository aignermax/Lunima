using CAP_Core.Routing;
using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Components.Connections;

/// <summary>
/// Stops retrying wires the router cannot place with their current pins. A wire that fails
/// again with unchanged pins, or in every ordering the cascade tried, is remembered in
/// <see cref="UnroutableWires"/>; from then on it keeps its blocked placeholder (still drawn
/// red with its failure reason), never triggers or joins the ordering cascade, and the
/// crossing-chain pass skips it once its chain search failed too — until one of its pins
/// moves or the user re-routes it explicitly (<see cref="RequestReroute"/>).
/// </summary>
public partial class WaveguideConnectionManager
{
    /// <summary>Fewest cascade attempts that must fail a wire before it counts as unroutable.</summary>
    private const int MinCascadeAttemptsForUnroutable = 2;

    /// <summary>Ids of the wires blocked in every cascade attempt of the current pass (null: no attempt yet).</summary>
    private HashSet<Guid>? _cascadePersistentFailures;

    private int _cascadeAttemptsRecorded;

    /// <summary>Wires proven unroutable with their current pins.</summary>
    public UnroutableWireMemory UnroutableWires { get; } = new();

    /// <summary>Set when a wire was added or removed since the last pass (see <see cref="NoteWireSetChanged"/>).</summary>
    private volatile bool _wireSetChanged;

    internal bool HasPendingWireSetChange => _wireSetChanged;

    /// <summary>Restores pass-start failure knowledge after private graph construction, without inventing an edit.</summary>
    internal void RestoreFailureState(IEnumerable<UnroutableWireMemory.SnapshotEntry> entries, bool pendingWireSetChange)
    {
        UnroutableWires.RestoreState(entries);
        _wireSetChanged = pendingWireSetChange;
    }

    /// <summary>True during a pass that follows an added or removed wire.</summary>
    private bool _wireSetChangedThisPass;

    /// <summary>
    /// A wire was added or removed: that changes the contention every blocked wire failed on,
    /// so the next pass forgets the unroutable wires and gives every blocked wire the full
    /// search again (cascade included).
    /// </summary>
    private void NoteWireSetChanged() => _wireSetChanged = true;

    /// <summary>Starts a pass: consumes <see cref="_wireSetChanged"/>.</summary>
    private void BeginUnroutablePass()
    {
        _wireSetChangedThisPass = _wireSetChanged;
        _wireSetChanged = false;
        if (_wireSetChangedThisPass)
            UnroutableWires.Clear();
    }

    /// <summary>
    /// Explicit re-route of one wire: forgets that it was proven unroutable and drops its
    /// route, so the next routing pass searches it from scratch (cascade included).
    /// </summary>
    public void RequestReroute(WaveguideConnection connection)
    {
        UnroutableWires.Forget(connection);
        _router.PathfindingGrid?.RemoveWaveguideObstacle(connection.Id);
        connection.InvalidateRoute();
    }

    /// <summary>Wires the running ordering cascade leaves alone (see <see cref="IsKeptOutOfRerouting"/>).</summary>
    private List<WaveguideConnection> _cascadeKept = new();

    /// <summary>
    /// Phase 2 of the full re-route over every wire that is not kept out of it. The kept ones
    /// keep their routes and stay obstacles in every attempt.
    /// </summary>
    private void RouteWithOrderingCascade(
        WaveguideRouter router, Action? progressCallback, CancellationToken cancellationToken)
    {
        _cascadeKept = SnapshotConnections().Where(IsKeptOutOfRerouting).ToList();
        try
        {
            RunOrderingCascade(router, progressCallback, cancellationToken);
        }
        finally
        {
            // The cascade's final apply re-registers only the wires it routed.
            RegisterCascadeKeptObstacles(router.PathfindingGrid);
            _cascadeKept = new List<WaveguideConnection>();
        }
    }

    /// <summary>
    /// Wires no re-route touches: those proven unroutable (they keep their placeholder) and
    /// settled pieces of routing-placed crossings (they keep their route until it goes stale).
    /// </summary>
    private bool IsKeptOutOfRerouting(WaveguideConnection connection) =>
        IsKnownUnroutable(connection) || IsSettledPiece(connection);

    /// <summary>The wires the ordering cascade re-routes: all but the kept ones.</summary>
    private List<WaveguideConnection> CascadeParticipants()
    {
        var kept = _cascadeKept.ToHashSet();
        return SnapshotConnections().Where(c => !kept.Contains(c)).ToList();
    }

    /// <summary>
    /// Puts the kept routes onto <paramref name="grid"/> (a cascade attempt starts from a clear
    /// one). Placeholder lines of unroutable wires are no geometry and stay off it.
    /// </summary>
    private void RegisterCascadeKeptObstacles(PathfindingGrid? grid)
    {
        if (grid == null) return;
        foreach (var kept in _cascadeKept.Where(c => c.IsPathValid && c.RoutedPath is { IsBlockedFallback: false }))
            grid.AddWaveguideObstacle(kept.Id, kept.RoutedPath!.Segments, ObstacleWidthFor(kept));
    }

    /// <summary>
    /// True when the wire carries its blocked placeholder at unchanged pins and is remembered
    /// as unroutable — the incremental pass keeps it instead of searching it again.
    /// </summary>
    private bool IsKnownUnroutable(WaveguideConnection connection) =>
        IsBlockedAtUnchangedPins(connection) && UnroutableWires.IsRemembered(connection);

    /// <summary>True when the wire is blocked and its placeholder still ends at both pins.</summary>
    private static bool IsBlockedAtUnchangedPins(WaveguideConnection connection)
    {
        var path = connection.RoutedPath;
        if (path is not { IsBlockedFallback: true } || path.Segments.Count == 0 || !connection.IsPathValid)
            return false;
        var (startX, startY) = connection.StartPin.GetAbsolutePosition();
        var (endX, endY) = connection.EndPin.GetAbsolutePosition();
        return Distance(path.Segments[0].StartPoint.X, path.Segments[0].StartPoint.Y, startX, startY) <= EndpointToleranceMicrometers
               && Distance(path.Segments[^1].EndPoint.X, path.Segments[^1].EndPoint.Y, endX, endY) <= EndpointToleranceMicrometers;
    }

    /// <summary>Narrows the set of wires blocked in every cascade attempt so far by one attempt's failures.</summary>
    private void RecordCascadeFailures(IEnumerable<WaveguideConnection> failed)
    {
        var blocked = failed.Where(c => c.IsBlockedFallback).Select(c => c.Id).ToHashSet();
        if (_cascadePersistentFailures == null)
            _cascadePersistentFailures = blocked;
        else
            _cascadePersistentFailures.IntersectWith(blocked);
        _cascadeAttemptsRecorded++;
    }

    /// <summary>
    /// After a complete pass: remembers every wire the cascade could not place in any of at
    /// least <see cref="MinCascadeAttemptsForUnroutable"/> orderings and that no later pass
    /// (crossing chains, contention repair) connected either.
    /// </summary>
    private void RememberPersistentCascadeFailures(CancellationToken cancellationToken)
    {
        var failures = _cascadePersistentFailures;
        int attempts = _cascadeAttemptsRecorded;
        _cascadePersistentFailures = null;
        _cascadeAttemptsRecorded = 0;
        if (failures == null || attempts < MinCascadeAttemptsForUnroutable || cancellationToken.IsCancellationRequested)
            return;
        foreach (var connection in SnapshotConnections())
        {
            if (failures.Contains(connection.Id) && IsBlockedAtUnchangedPins(connection))
                UnroutableWires.Remember(connection);
        }
    }
}
