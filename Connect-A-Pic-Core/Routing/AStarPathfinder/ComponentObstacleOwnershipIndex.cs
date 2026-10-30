using CAP_Core.Components.Core;

namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Owns component body/padding claims and pin corridors independently of grid occupancy.
/// The grid checks its cell state first; this index supplies the owner-specific verdict.
/// </summary>
internal sealed class ComponentObstacleOwnershipIndex
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
    internal void Register(
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
    internal void Unregister(Component component)
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
    internal void Clear()
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
    internal HashSet<(int x, int y)> GetPinCorridorCells(IEnumerable<PhysicalPin> pins)
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
    /// True when a component-blocked cell stays blocking despite the corridor tolerance:
    /// either it lies outside the tolerated corridors, or a body (not just padding) claims
    /// it. A blocked cell without any ownership record stays blocking — conservative.
    /// </summary>
    internal bool BlocksDespiteCorridorTolerance(
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
    /// True when the cell carries a component-BODY claim by any component other than
    /// <paramref name="ownComponent"/>. A blocked cell without an ownership record counts
    /// as a foreign body — conservative.
    /// </summary>
    internal bool HasForeignBodyClaim(int gridX, int gridY, Component? ownComponent)
    {
        lock (_ownershipLock)
        {
            bool foreignBody = AnyClaim(gridX, gridY,
                (owner, isBody) => isBody && owner != ownComponent, out var claimed);
            return !claimed || foreignBody;
        }
    }

    internal bool HasPaddingClaimsOnly(int gridX, int gridY)
    {
        lock (_ownershipLock)
        {
            bool anyBody = AnyClaim(gridX, gridY, (_, isBody) => isBody, out var claimed);
            return claimed && !anyBody;
        }
    }

    internal bool HasOutlineBodyClaim(int gridX, int gridY)
    {
        lock (_ownershipLock)
            return AnyClaim(gridX, gridY,
                (owner, body) => body && owner.OutlinePolygons is { Count: > 0 }, out _);
    }

    internal bool HasForeignClaim(int gridX, int gridY, IReadOnlySet<Component> ownComponents)
    {
        lock (_ownershipLock)
        {
            bool foreign = AnyClaim(gridX, gridY, (owner, _) => !ownComponents.Contains(owner), out var claimed);
            return !claimed || foreign;
        }
    }

    /// <summary>
    /// Prepares identity replacements without enumerating cells. The caller exclusively owns
    /// the completed grid and uses these same footprint objects in its occupancy bookkeeping.
    /// Preparation and application retain the existing lock-free ownership-transfer contract.
    /// </summary>
    internal Dictionary<ComponentFootprint, ComponentFootprint> PrepareReboundFootprints(
        Func<Component, Component> map) =>
        _footprints.Values.ToDictionary(footprint => footprint, footprint => footprint.WithOwner(map(footprint.Owner)));

    /// <summary>Applies identities after the grid has rebound its cell and pin-zone owners.</summary>
    internal void RebindOwners(IReadOnlyDictionary<ComponentFootprint, ComponentFootprint> replacements,
        Func<Component, Component> map, Func<PhysicalPin, PhysicalPin> mapPin)
    {
        _footprints.Clear();
        foreach (var footprint in replacements.Values) _footprints.Add(footprint.Owner, footprint);
        foreach (var bucket in _footprintBuckets.Values)
            for (var index = 0; index < bucket.Count; index++) bucket[index] = replacements[bucket[index]];
        var corridors = _pinCorridorCells.Select(pair => (Pin: mapPin(pair.Key), Cells: pair.Value)).ToArray();
        _pinCorridorCells.Clear();
        foreach (var entry in corridors) _pinCorridorCells.Add(entry.Pin, entry.Cells);
        var componentPins = _componentCorridorPins.Select(pair =>
            (Owner: map(pair.Key), Pins: pair.Value.Select(mapPin).ToList())).ToArray();
        _componentCorridorPins.Clear();
        foreach (var entry in componentPins) _componentCorridorPins.Add(entry.Owner, entry.Pins);
    }
}
