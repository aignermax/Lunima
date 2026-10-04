using System.Diagnostics;
using CAP_Core.Routing;
using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Components.Connections;

/// <summary>
/// Targeted rip-up-and-reroute repair for contention-blocked wires, run once after the
/// normal routing passes (issue #1276). The ordering cascade optimizes globally — every
/// permutation re-routes the whole design, so a locally fixable conflict can survive all
/// of them. This pass works locally instead: for a blocked wire whose failure is
/// <see cref="RoutingFailureReason.Contention"/> it rips up only the few routed siblings
/// the wire crosses or crowds at grid resolution, routes the blocked wire first, then
/// re-routes the ripped-up ones. The result is accepted only when the blocked count strictly decreases
/// and no new crossing appears; otherwise the previous routes are restored exactly, so
/// the pass can never make a design worse. Endpoint-blocked wires (a pin sealed by a
/// component footprint) and frozen/manual routes are never touched — no wire ordering
/// can free a footprint, and manual edits are sacred.
/// <para>
/// The budget is hard: every attempt routes on a token that cancels when the remaining
/// budget expires, so one stubborn in-flight A* search cannot overrun the pass — a
/// budget-cut attempt is restored exactly and the caller's own token stays uncancelled.
/// And an attempt that failed once is not repeated verbatim: its fingerprint (blocked
/// wire, ripped-up siblings, their pin positions) is remembered, and an identical
/// attempt is skipped on later passes until an involved component moves.
/// </para>
/// </summary>
public partial class WaveguideConnectionManager
{
    /// <summary>Maximum routed siblings one repair attempt rips up alongside the blocked wire.</summary>
    private const int MaxContentionRipUpSiblings = 3;

    /// <summary>Maximum blocked wires a single pass attempts to repair.</summary>
    private const int MaxContentionRepairAttemptsPerPass = 8;

    /// <summary>
    /// Fingerprints of attempts that failed in this manager. An identical attempt (same
    /// blocked wire, same ripped-up siblings, same pin positions) is skipped on later
    /// passes instead of being paid for again; moving any involved component changes the
    /// fingerprint's pin positions, which re-enables the attempt.
    /// </summary>
    private readonly HashSet<ContentionRepairAttemptKey> _knownFailedAttempts = new();

    /// <summary>
    /// Wall-clock budget for one repair pass. Checked before every attempt, and enforced
    /// INSIDE one through a linked cancellation token (<see cref="TryRepairContentionWire"/>)
    /// that interrupts an in-flight route when the remaining budget expires. One attempt is
    /// a handful of A* routes and legitimately takes seconds, so the budget must be
    /// generous — the bound on attempt COUNT is <see cref="MaxContentionRepairAttemptsPerPass"/>.
    /// Settable so tests on slow CI runners can pin the repair outcome independently of
    /// machine speed.
    /// </summary>
    internal TimeSpan ContentionRepairTimeBudget { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Number of rip-up-and-reroute attempts the last <see cref="RecalculateAllTransmissions"/>
    /// pass ran. Diagnostic hook for tests and telemetry.
    /// </summary>
    public int LastContentionRepairAttemptCount { get; private set; }

    /// <summary>
    /// Number of attempts the last pass accepted (blocked count strictly decreased without
    /// a new crossing). Diagnostic hook for tests and telemetry.
    /// </summary>
    public int LastContentionRepairAcceptCount { get; private set; }

    /// <summary>
    /// Runs the bounded repair pass over every contention-blocked wire. Must be called
    /// after <see cref="MarkUnresolvedSiblingCrossings"/> so crossing-stamped wires carry
    /// their contention classification.
    /// </summary>
    internal void RepairContentionBlockedWires(CancellationToken cancellationToken = default)
    {
        var grid = _router.PathfindingGrid;
        if (!UseSequentialRouting || grid == null)
            return;

        LastContentionRepairAttemptCount = 0;
        LastContentionRepairAcceptCount = 0;
        var budget = Stopwatch.StartNew();

        // Snapshot: runs on the routing thread while UI commands may mutate the list.
        foreach (var blocked in SnapshotConnections())
        {
            if (cancellationToken.IsCancellationRequested
                || LastContentionRepairAttemptCount >= MaxContentionRepairAttemptsPerPass
                || budget.Elapsed >= ContentionRepairTimeBudget)
                return;
            if (!IsContentionRepairCandidate(blocked))
                continue;

            var siblings = FindConflictingSiblings(blocked, SnapshotConnections());
            if (siblings.Count == 0)
                continue; // No wire conflict to rip up — nothing this pass can try.

            // A fingerprint the pass already paid for and lost is not tried again while
            // nothing it involves changed — on a design with one permanently blocked wire
            // every full re-route would otherwise burn the whole budget for 0 accepts.
            var attemptKey = ContentionRepairAttemptKey.For(blocked, siblings);
            if (!_knownFailedAttempts.Add(attemptKey))
                continue;

            LastContentionRepairAttemptCount++;
            if (TryRepairContentionWire(blocked, siblings, grid, budget, cancellationToken))
            {
                LastContentionRepairAcceptCount++;
                _knownFailedAttempts.Remove(attemptKey);
            }
        }
    }

    /// <summary>
    /// One repair attempt: rip up the blocked wire and its nearest crossing siblings,
    /// route the blocked wire first, re-route the siblings, and keep the result only on
    /// a strict improvement. Returns true when the attempt was accepted.
    /// <para>
    /// The attempt routes on a token linked to the caller's token that cancels when the
    /// remaining pass budget expires: an in-flight A* search is interrupted instead of
    /// overrunning the budget by its own routing time. A budget-cut (or caller-cancelled)
    /// attempt is restored exactly; the caller's token itself is never cancelled by the
    /// budget — only the linked attempt token fires.
    /// </para>
    /// </summary>
    internal bool TryRepairContentionWire(
        WaveguideConnection blocked,
        List<WaveguideConnection> siblings,
        PathfindingGrid grid,
        Stopwatch budget,
        CancellationToken cancellationToken)
    {
        var all = SnapshotConnections();
        var touched = new List<WaveguideConnection> { blocked };
        touched.AddRange(siblings);
        int blockedBefore = touched.Count(c => c.IsBlockedFallback);
        int crossingsBefore = CountCrossingsInvolving(touched, all);
        var saved = touched.ToDictionary(c => c, c => c.RoutedPath!.DeepCopy());

        foreach (var connection in touched)
            grid.RemoveWaveguideObstacle(connection.Id);

        using var attemptTokens = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var remaining = ContentionRepairTimeBudget - budget.Elapsed;
        if (remaining <= TimeSpan.Zero)
            attemptTokens.Cancel();
        else
            attemptTokens.CancelAfter(remaining);

        bool completed = RouteTouched(touched, grid, attemptTokens.Token);

        int crossingsAfter = CountCrossingsInvolving(touched, all);
        bool accept = completed
            && !cancellationToken.IsCancellationRequested
            && touched.All(c => c.IsPathValid)
            && touched.Count(c => c.IsBlockedFallback) < blockedBefore
            && crossingsAfter <= crossingsBefore;
        if (!accept)
        {
            RestoreRoutes(touched, saved, grid);
            return false;
        }

        // Router honesty: the re-routes cleared every touched stamp, so an accepted
        // attempt that left its crossing in place (a non-planar pair the router cannot
        // untangle) would render as a clean route. Re-stamp the crossing side exactly
        // like MarkUnresolvedSiblingCrossings does. A repair that strictly reduced the
        // crossing count behaves as before.
        if (crossingsAfter == crossingsBefore)
            RestampRemainingCrossings(touched, all);
        return true;
    }

    /// <summary>
    /// Re-applies the blocked-fallback stamp to the re-routable side of every crossing
    /// pair that involves a touched connection — the same verdict
    /// MarkUnresolvedSiblingCrossings would give, restricted to the geometry this
    /// attempt re-routed.
    /// </summary>
    private static void RestampRemainingCrossings(
        List<WaveguideConnection> touched, List<WaveguideConnection> all)
    {
        // Each unordered pair is judged once: when both sides were touched, the second
        // visit would flip PickReroutableSide's preference and stamp BOTH wires.
        var judged = new HashSet<(Guid, Guid)>();
        foreach (var connection in touched)
        {
            if (connection.RoutedPath == null || !connection.IsPathValid)
                continue;
            foreach (var other in all)
            {
                if (ReferenceEquals(other, connection)
                    || other.RoutedPath == null
                    || !other.IsPathValid)
                    continue;
                var pair = connection.Id.CompareTo(other.Id) < 0
                    ? (connection.Id, other.Id)
                    : (other.Id, connection.Id);
                if (!judged.Add(pair))
                    continue;
                if (!PathIntersectionDetector.Crosses(connection.RoutedPath, other.RoutedPath))
                    continue;
                var target = PickReroutableSide(connection, other);
                if (target != null)
                {
                    target.RoutedPath!.IsBlockedFallback = true;
                    target.RoutedPath.FailureReason = RoutingFailureReason.Contention;
                }
            }
        }
    }

    /// <summary>
    /// Routes the blocked wire first, then the ripped-up siblings, registering each valid
    /// result as a grid obstacle. False when the attempt token (caller cancellation OR the
    /// expired time budget) cut the attempt short — the attempt must then be restored,
    /// because an un-rerouted sibling would be missing from the obstacle grid and a
    /// cancelled route's fallback geometry is not a repair result.
    /// </summary>
    private bool RouteTouched(
        List<WaveguideConnection> touched,
        PathfindingGrid grid,
        CancellationToken attemptToken)
    {
        foreach (var connection in touched)
        {
            if (attemptToken.IsCancellationRequested)
                return false;
            connection.RecalculateTransmission(_router, cancellationToken: attemptToken);
            if (attemptToken.IsCancellationRequested)
                return false;
            if (connection.IsPathValid && connection.RoutedPath != null)
            {
                grid.AddWaveguideObstacle(
                    connection.Id, connection.RoutedPath.Segments, ObstacleWidthFor(connection));
            }
        }
        return true;
    }

    /// <summary>Puts back the exact pre-attempt geometry and its grid registration.</summary>
    private void RestoreRoutes(
        List<WaveguideConnection> touched,
        Dictionary<WaveguideConnection, RoutedPath> saved,
        PathfindingGrid grid)
    {
        foreach (var connection in touched)
        {
            connection.ReplaceRoutedPath(saved[connection]);
            grid.AddWaveguideObstacle(
                connection.Id, connection.RoutedPath!.Segments, ObstacleWidthFor(connection));
        }
    }

    /// <summary>
    /// Counts proper crossings of every routed pair that involves at least one touched
    /// connection. Pairs of untouched connections cannot change during an attempt, so
    /// comparing this count before and after answers "did a new crossing appear".
    /// </summary>
    private static int CountCrossingsInvolving(
        List<WaveguideConnection> touched, List<WaveguideConnection> all)
    {
        int crossings = 0;
        var counted = new HashSet<(Guid, Guid)>();
        foreach (var connection in touched)
        {
            if (connection.RoutedPath == null || !connection.IsPathValid)
                continue;
            foreach (var other in all)
            {
                if (ReferenceEquals(other, connection) || other.RoutedPath == null || !other.IsPathValid)
                    continue;
                var pair = connection.Id.CompareTo(other.Id) < 0
                    ? (connection.Id, other.Id)
                    : (other.Id, connection.Id);
                if (!counted.Add(pair))
                    continue;
                if (PathIntersectionDetector.Crosses(connection.RoutedPath, other.RoutedPath))
                    crossings++;
            }
        }
        return crossings;
    }

    /// <summary>
    /// A wire the repair pass may work on: an unfrozen AUTO route that is blocked by
    /// contention. Frozen/manual routes and crossing sub-connections (whose geometry is
    /// owned by the crossing record) are never touched.
    /// </summary>
    private bool IsContentionRepairCandidate(WaveguideConnection connection) =>
        connection.Type == WaveguideType.Auto
        && !connection.IsRouteFrozen
        && !connection.IsCrossChipletFacetLink
        && connection.RoutedPath is { IsBlockedFallback: true, FailureReason: RoutingFailureReason.Contention }
        && connection.IsPathValid
        && !IsCrossingParticipant(connection);

    /// <summary>An unfrozen AUTO route with valid geometry the attempt may rip up and re-route.</summary>
    private bool IsReroutableSibling(WaveguideConnection connection) =>
        connection.Type == WaveguideType.Auto
        && !connection.IsRouteFrozen
        && !connection.IsCrossChipletFacetLink
        && connection.RoutedPath != null
        && connection.IsPathValid
        && !IsCrossingParticipant(connection);

    private bool IsCrossingParticipant(WaveguideConnection connection) =>
        CrossingInsertion?.IsSubConnection(connection) == true;

    /// <summary>
    /// The up-to-<see cref="MaxContentionRipUpSiblings"/> re-routable siblings the blocked
    /// wire conflicts with, nearest first. A conflict is not only a proper crossing: a
    /// sibling whose centerline passes within both obstacle half-bands plus one grid cell
    /// plus the minimum spacing crowds the blocked wire's channel at grid resolution —
    /// the sub-cell contention the rasterized grid reports as blocked although the two
    /// geometries never touch. Distant siblings are skipped on inflated bounds before the
    /// exact polyline distance is computed.
    /// </summary>
    private List<WaveguideConnection> FindConflictingSiblings(
        WaveguideConnection blocked, List<WaveguideConnection> all)
    {
        var blockedBounds = PathBounds(blocked.RoutedPath!);
        var candidates = new List<(WaveguideConnection Connection, double Distance)>();
        foreach (var sibling in all)
        {
            if (ReferenceEquals(sibling, blocked) || !IsReroutableSibling(sibling))
                continue;
            double conflictDistance = ConflictDistanceMicrometers(blocked, sibling);
            if (!BoundsOverlap(Inflate(blockedBounds, conflictDistance), PathBounds(sibling.RoutedPath!)))
                continue;
            double distance = PathIntersectionDetector.MinimumDistance(
                blocked.RoutedPath!, sibling.RoutedPath!);
            if (distance < conflictDistance)
                candidates.Add((sibling, distance));
        }
        return candidates
            .OrderBy(c => c.Distance)
            .Take(MaxContentionRipUpSiblings)
            .Select(c => c.Connection)
            .ToList();
    }

    /// <summary>
    /// Centerline distance below which two wires conflict at grid resolution: each side's
    /// registered obstacle half-band, one pathfinding cell (the rasterization quantum that
    /// decides whether a free lane fits between the bands), and the minimum waveguide
    /// spacing a new route must keep.
    /// </summary>
    private double ConflictDistanceMicrometers(WaveguideConnection first, WaveguideConnection second) =>
        (ObstacleWidthFor(first) + ObstacleWidthFor(second)) / 2
        + _router.AStarCellSize
        + _router.MinWaveguideSpacingMicrometers;

    private static (double MinX, double MinY, double MaxX, double MaxY) PathBounds(RoutedPath path)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var segment in path.Segments)
        {
            var bounds = PathSegmentBounds.Of(segment);
            minX = Math.Min(minX, bounds.MinX);
            minY = Math.Min(minY, bounds.MinY);
            maxX = Math.Max(maxX, bounds.MaxX);
            maxY = Math.Max(maxY, bounds.MaxY);
        }
        return (minX, minY, maxX, maxY);
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) Inflate(
        (double MinX, double MinY, double MaxX, double MaxY) bounds, double padding) =>
        (bounds.MinX - padding, bounds.MinY - padding, bounds.MaxX + padding, bounds.MaxY + padding);

    private static bool BoundsOverlap(
        (double MinX, double MinY, double MaxX, double MaxY) first,
        (double MinX, double MinY, double MaxX, double MaxY) second) =>
        first.MinX <= second.MaxX && first.MaxX >= second.MinX
        && first.MinY <= second.MaxY && first.MaxY >= second.MinY;

    /// <summary>
    /// Fingerprint of one repair attempt: the blocked wire's id, the ripped-up siblings'
    /// ids (order-independent), and a quantization of every involved pin position. Two
    /// passes over an unchanged design produce the same fingerprint, so a known failure is
    /// skipped; moving any involved component changes its pins' positions and thereby the
    /// fingerprint, which re-enables the attempt. Route GEOMETRY is deliberately not part
    /// of the key: the re-routes inside an attempt must not wash a failure out of memory.
    /// </summary>
    private readonly struct ContentionRepairAttemptKey : IEquatable<ContentionRepairAttemptKey>
    {
        /// <summary>
        /// Pin positions are quantized to this grid, so sub-quantum float noise in the
        /// pin math cannot fragment an otherwise identical attempt into different keys.
        /// </summary>
        private const double PositionQuantumMicrometers = 0.5;

        private readonly Guid _blockedId;
        private readonly Guid[] _siblingIds;
        private readonly int _pinPositionHash;

        private ContentionRepairAttemptKey(Guid blockedId, Guid[] siblingIds, int pinPositionHash)
        {
            _blockedId = blockedId;
            _siblingIds = siblingIds;
            _pinPositionHash = pinPositionHash;
        }

        /// <summary>Builds the fingerprint of an attempt over <paramref name="blocked"/> and its siblings.</summary>
        public static ContentionRepairAttemptKey For(
            WaveguideConnection blocked, List<WaveguideConnection> siblings)
        {
            var ordered = siblings.OrderBy(s => s.Id).ToArray();
            var hash = new HashCode();
            AddPinPositions(ref hash, blocked);
            foreach (var sibling in ordered)
                AddPinPositions(ref hash, sibling);
            return new ContentionRepairAttemptKey(
                blocked.Id, ordered.Select(s => s.Id).ToArray(), hash.ToHashCode());

            static void AddPinPositions(ref HashCode hash, WaveguideConnection connection)
            {
                var (startX, startY) = connection.StartPin.GetAbsolutePosition();
                var (endX, endY) = connection.EndPin.GetAbsolutePosition();
                hash.Add(Quantize(startX));
                hash.Add(Quantize(startY));
                hash.Add(Quantize(endX));
                hash.Add(Quantize(endY));
            }

            static long Quantize(double micrometers) =>
                (long)Math.Round(micrometers / PositionQuantumMicrometers);
        }

        public bool Equals(ContentionRepairAttemptKey other) =>
            _blockedId == other._blockedId
            && _pinPositionHash == other._pinPositionHash
            && _siblingIds.SequenceEqual(other._siblingIds);

        public override bool Equals(object? obj) =>
            obj is ContentionRepairAttemptKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(_blockedId);
            hash.Add(_pinPositionHash);
            foreach (var id in _siblingIds)
                hash.Add(id);
            return hash.ToHashCode();
        }
    }
}
