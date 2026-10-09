using CAP_Core.Components.Core;

namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Ownership-aware cell bookkeeping for <see cref="PathfindingGrid"/>. Rasterizing a
/// component records for every blocked cell WHO blocked it and HOW (body rectangle vs.
/// surrounding padding band), and keeps the carved pin-corridor cells per pin. Read-only
/// predicates can then distinguish "the route hugs its own pin inside that pin's corridor,
/// and only a foreign padding band reaches across" (tolerated — the corridor belongs to the
/// route) from "a foreign component body occupies the corridor" (a real collision).
/// The cell states themselves are untouched — routing cost and obstacle semantics do not
/// change; this data only feeds the collision verdicts of already-routed paths.
/// </summary>
public partial class PathfindingGrid
{
    /// <summary>Side length (cells) of the buckets that index footprints by area.</summary>
    private const int FootprintBucketCells = 64;

    // Which footprints cover a bucket: an ownership query only scans the footprints of
    // the bucket its cell falls into (a cell can be claimed by several overlapping components).
    private readonly Dictionary<(int bx, int by), List<ComponentFootprint>> _footprintBuckets = new();
    private readonly Dictionary<Component, ComponentFootprint> _footprints = new();

    // Rasterized pin-corridor cells per pin — the same geometry the obstacle rasterization
    // carves out of its own component's blocked cells.
    private readonly Dictionary<PhysicalPin, HashSet<(int x, int y)>> _pinCorridorCells = new();

    // Which pins a component registered corridors for, so removal does not depend on the
    // component's current (possibly already mutated) pin list.
    private readonly Dictionary<Component, List<PhysicalPin>> _componentCorridorPins = new();
    private readonly object _ownershipLock = new();

    /// <summary>
    /// Records a component's freshly blocked footprint and its pin corridors. Called by the
    /// obstacle rasterization right after the cells were marked.
    /// </summary>
    private void RegisterComponentOwnership(
        ComponentFootprint footprint,
        Dictionary<PhysicalPin, HashSet<(int x, int y)>> pinCorridors)
    {
        lock (_ownershipLock)
        {
            RemoveFootprint(footprint.Owner);
            _footprints[footprint.Owner] = footprint;
            foreach (var bucket in BucketsOf(footprint))
            {
                if (!_footprintBuckets.TryGetValue(bucket, out var list))
                    _footprintBuckets[bucket] = list = new List<ComponentFootprint>();
                list.Add(footprint);
            }

            foreach (var (pin, cells) in pinCorridors)
                _pinCorridorCells[pin] = cells;
            _componentCorridorPins[footprint.Owner] = pinCorridors.Keys.ToList();
        }
    }

    /// <summary>
    /// Drops a component's ownership claim and pin corridors (its cells were just freed in
    /// the cell grid). Overlapping components keep their own claims; a freed cell is no
    /// longer blocked, so the predicates never consult claims on it.
    /// </summary>
    private void UnregisterComponentOwnership(Component component, IEnumerable<(int x, int y)>? freedCells)
    {
        lock (_ownershipLock)
        {
            RemoveFootprint(component);
            if (_componentCorridorPins.Remove(component, out var pins))
            {
                foreach (var pin in pins)
                    _pinCorridorCells.Remove(pin);
            }
        }
    }

    /// <summary>Clears all ownership and pin-corridor bookkeeping (grid rebuild).</summary>
    private void ClearOwnership()
    {
        lock (_ownershipLock)
        {
            _footprintBuckets.Clear();
            _footprints.Clear();
            _pinCorridorCells.Clear();
            _componentCorridorPins.Clear();
        }
    }

    private void RemoveFootprint(Component component)
    {
        if (!_footprints.Remove(component, out var footprint)) return;
        foreach (var bucket in BucketsOf(footprint))
        {
            if (_footprintBuckets.TryGetValue(bucket, out var list))
                list.Remove(footprint);
        }
    }

    private static IEnumerable<(int bx, int by)> BucketsOf(ComponentFootprint footprint)
    {
        var (x1, y1, x2, y2) = footprint.Padded;
        for (int bx = FloorDiv(x1); bx <= FloorDiv(x2); bx++)
        for (int by = FloorDiv(y1); by <= FloorDiv(y2); by++)
            yield return (bx, by);
    }

    private static int FloorDiv(int cell) => (int)Math.Floor(cell / (double)FootprintBucketCells);

    /// <summary>
    /// Scans the component claims on a cell: <paramref name="match"/> gets each owner and
    /// whether the cell lies in its body; the scan stops at the first match. Must run under
    /// <see cref="_ownershipLock"/>. <paramref name="claimed"/> reports whether anybody
    /// claims the cell at all.
    /// </summary>
    /// <returns>True when a claim satisfied <paramref name="match"/>.</returns>
    private bool AnyClaim(int gridX, int gridY, Func<Component, bool, bool> match, out bool claimed)
    {
        claimed = false;
        if (!_footprintBuckets.TryGetValue((FloorDiv(gridX), FloorDiv(gridY)), out var list))
            return false;
        foreach (var footprint in list)
        {
            if (!footprint.Claims(gridX, gridY, out var isBody)) continue;
            claimed = true;
            if (match(footprint.Owner, isBody)) return true;
        }
        return false;
    }

    /// <summary>
    /// Union of the rasterized pin-corridor cells of the given pins, as carved when their
    /// components were registered as obstacles. Pins without a registered corridor (their
    /// component is no routing obstacle) contribute nothing.
    /// </summary>
    public HashSet<(int x, int y)> GetPinCorridorCells(IEnumerable<PhysicalPin> pins)
    {
        var result = new HashSet<(int x, int y)>();
        lock (_ownershipLock)
        {
            foreach (var pin in pins)
            {
                if (_pinCorridorCells.TryGetValue(pin, out var cells))
                    result.UnionWith(cells);
            }
        }
        return result;
    }

    /// <summary>
    /// Like <see cref="IsBlockedByComponent(int, int)"/>, but tolerates a route hugging its
    /// own pins: a blocked cell inside one of the <paramref name="toleratedCorridorCells"/>
    /// (the corridors of the route's own endpoint pins) counts as blocked only when a
    /// component BODY claims it — foreign padding reaching across the corridor does not.
    /// Frozen group path markings (state 3) stay blocking regardless.
    /// </summary>
    public bool IsBlockedByComponent(int gridX, int gridY, IReadOnlySet<(int x, int y)> toleratedCorridorCells)
    {
        if (!IsInBounds(gridX, gridY)) return true;
        byte state = _cells[gridX, gridY];
        if (state == 3) return true;
        if (state != 1) return false;
        return BlocksDespiteCorridorTolerance(gridX, gridY, toleratedCorridorCells);
    }

    /// <summary>
    /// Like <see cref="IsBlockedByComponentOnly(int, int)"/> (component geometry only,
    /// excluding frozen group path markings) with the same own-pin-corridor tolerance as
    /// the <see cref="IsBlockedByComponent(int, int)"/> overload above.
    /// </summary>
    public bool IsBlockedByComponentOnly(int gridX, int gridY, IReadOnlySet<(int x, int y)> toleratedCorridorCells)
    {
        if (!IsInBounds(gridX, gridY)) return true;
        if (_cells[gridX, gridY] != 1) return false;
        return BlocksDespiteCorridorTolerance(gridX, gridY, toleratedCorridorCells);
    }

    /// <summary>
    /// True when a component-blocked cell stays blocking despite the corridor tolerance:
    /// either it lies outside the tolerated corridors, or a body (not just padding) claims
    /// it. A blocked cell without any ownership record stays blocking — conservative.
    /// </summary>
    private bool BlocksDespiteCorridorTolerance(
        int gridX, int gridY, IReadOnlySet<(int x, int y)> toleratedCorridorCells)
    {
        if (!toleratedCorridorCells.Contains((gridX, gridY)))
            return true;
        lock (_ownershipLock)
        {
            bool anyBody = AnyClaim(gridX, gridY, (_, isBody) => isBody, out var claimed);
            return !claimed || anyBody;
        }
    }

    /// <summary>
    /// True when the pin's escape channel — the corridor the A* attempt punches through
    /// component geometry (length × width in µm along the pin's outward axis) — is sealed
    /// by component footprints on EVERY cross-section (widened by one cell beyond the
    /// punched walls and one cell past the far end, so spilling out sideways or through
    /// the end cap counts as open), with at least one FOREIGN component body among the
    /// blocking claims. A sealed pin is unreachable no matter how the remaining wires are
    /// ordered. Free cells, waveguide cells and frozen path markings leave a lane open
    /// (waveguide blockage is the contention case a re-ordering can fix); cells claimed
    /// only by the pin's own component or by padding bands seal the channel but do not
    /// count as foreign bodies.
    /// </summary>
    /// <param name="pin">The pin whose escape channel is checked.</param>
    /// <param name="corridorLengthMicrometers">Length of the punched corridor in µm.</param>
    /// <param name="corridorWidthMicrometers">Width of the punched corridor in µm.</param>
    public bool IsPinEscapeSealedByForeignBody(
        PhysicalPin pin, double corridorLengthMicrometers, double corridorWidthMicrometers)
    {
        var (pinX, pinY) = pin.GetAbsolutePosition();
        double angleRad = pin.GetAbsoluteAngle() * Math.PI / 180.0;
        double dx = Math.Cos(angleRad);
        double dy = Math.Sin(angleRad);
        double perpX = -dy;
        double perpY = dx;

        double halfWidth = corridorWidthMicrometers / 2 + CellSizeMicrometers;
        double scanLength = corridorLengthMicrometers + CellSizeMicrometers;
        bool sawForeignBody = false;

        for (double dist = 0; dist <= scanLength; dist += CellSizeMicrometers)
        {
            bool laneFree = false;
            for (double offset = -halfWidth; offset <= halfWidth; offset += CellSizeMicrometers)
            {
                var (gx, gy) = PhysicalToGrid(
                    pinX + dx * dist + perpX * offset,
                    pinY + dy * dist + perpY * offset);
                if (!IsInBounds(gx, gy) || _cells[gx, gy] != 1)
                {
                    laneFree = true;
                    break;
                }
                if (HasForeignBodyClaim(gx, gy, pin.ParentComponent))
                    sawForeignBody = true;
            }
            if (laneFree)
                return false;
        }
        return sawForeignBody;
    }

    /// <summary>
    /// True when the cell carries a component-BODY claim by any component other than
    /// <paramref name="ownComponent"/>. A blocked cell without an ownership record counts
    /// as a foreign body — conservative.
    /// </summary>
    private bool HasForeignBodyClaim(int gridX, int gridY, Component? ownComponent)
    {
        lock (_ownershipLock)
        {
            bool foreignBody = AnyClaim(gridX, gridY,
                (owner, isBody) => isBody && owner != ownComponent, out var claimed);
            return !claimed || foreignBody;
        }
    }
}
