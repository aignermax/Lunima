using CAP_Core.Components.Core;

namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Breakout reservations for <see cref="PathfindingGrid"/>: the S-bends and straight leads
/// planned in front of tight pin clusters (<see cref="PinBreakout.PinBreakoutPlan"/>) are
/// blocked with their own cell state, so no other wire — neither a foreign one nor a
/// sibling of the same cluster — takes the room a cluster wire needs to reach its pin.
/// A wire lifts the reservations of its own pins while it routes. Reservation cells are
/// never crossable (crossings only land on waveguide cells) and do not count as
/// component collisions. Cells are reference-counted per reservation, so overlapping
/// envelopes of neighbouring pins stay blocked until the last one goes.
/// </summary>
public partial class PathfindingGrid
{
    /// <summary>Cell state of a breakout reservation (blocked for A*, not a component, not crossable).</summary>
    public const byte BreakoutReservationCellState = 4;

    private const byte FreeCellState = 0;
    private const byte WaveguideCellState = 2;

    private readonly Dictionary<Guid, HashSet<(int x, int y)>> _breakoutReservations = new();
    private readonly Dictionary<(int x, int y), int> _breakoutReservationRefs = new();
    private readonly object _breakoutLock = new();

    /// <summary>Number of active breakout reservations (diagnostics and tests).</summary>
    public int BreakoutReservationCount
    {
        get { lock (_breakoutLock) return _breakoutReservations.Count; }
    }

    /// <summary>
    /// Reserves the cells under <paramref name="segments"/> (band of the given width) for
    /// <paramref name="reservationId"/>. Only free cells change state; cells a waveguide
    /// covers become reserved once that waveguide leaves.
    /// </summary>
    public void ReserveBreakout(Guid reservationId, IEnumerable<PathSegment> segments, double bandWidthMicrometers)
    {
        RemoveBreakoutReservation(reservationId);
        var cells = GetWaveguidePathCells(segments.ToList(), bandWidthMicrometers);
        cells.RemoveWhere(c => !IsInBounds(c.Item1, c.Item2));
        lock (_breakoutLock)
        {
            _breakoutReservations[reservationId] = cells;
            foreach (var cell in cells)
            {
                _breakoutReservationRefs[cell] = _breakoutReservationRefs.GetValueOrDefault(cell) + 1;
                if (_cells[cell.Item1, cell.Item2] == FreeCellState)
                    _cells[cell.Item1, cell.Item2] = BreakoutReservationCellState;
            }
        }
        WaveguideVersion++;
    }

    /// <summary>Drops one reservation; its cells fall back to free or to a covering waveguide.</summary>
    public void RemoveBreakoutReservation(Guid reservationId)
    {
        lock (_breakoutLock)
        {
            if (!_breakoutReservations.Remove(reservationId, out var cells)) return;
            var released = new List<(int x, int y)>();
            foreach (var cell in cells)
            {
                int refs = _breakoutReservationRefs.GetValueOrDefault(cell) - 1;
                if (refs > 0) { _breakoutReservationRefs[cell] = refs; continue; }
                _breakoutReservationRefs.Remove(cell);
                released.Add(cell);
            }
            ReleaseReservedCells(released);
        }
        WaveguideVersion++;
    }

    /// <summary>Drops every breakout reservation.</summary>
    public void ClearBreakoutReservations()
    {
        lock (_breakoutLock)
        {
            if (_breakoutReservations.Count == 0) return;
            var released = _breakoutReservationRefs.Keys.ToList();
            _breakoutReservations.Clear();
            _breakoutReservationRefs.Clear();
            ReleaseReservedCells(released);
        }
        WaveguideVersion++;
    }

    /// <summary>
    /// Temporarily frees the cells reserved ONLY by <paramref name="reservationIds"/> (a wire
    /// routing to its own pins). Cells another pin's reservation also covers stay blocked.
    /// Hand the result to <see cref="RestoreCells"/> afterwards.
    /// </summary>
    public Dictionary<(int x, int y), byte> LiftBreakoutReservations(IReadOnlyCollection<Guid> reservationIds)
    {
        var lifted = new Dictionary<(int x, int y), byte>();
        lock (_breakoutLock)
        {
            var coverage = new Dictionary<(int x, int y), int>();
            foreach (var id in reservationIds.Distinct())
            {
                if (!_breakoutReservations.TryGetValue(id, out var cells)) continue;
                foreach (var cell in cells)
                    coverage[cell] = coverage.GetValueOrDefault(cell) + 1;
            }
            var freed = coverage
                .Where(kv => _breakoutReservationRefs.GetValueOrDefault(kv.Key) == kv.Value
                    && _cells[kv.Key.x, kv.Key.y] == BreakoutReservationCellState)
                .Select(kv => kv.Key)
                .ToHashSet();
            var covered = CellsCoveredByWaveguides(freed);
            foreach (var cell in freed)
            {
                lifted[cell] = BreakoutReservationCellState;
                _cells[cell.x, cell.y] = covered.Contains(cell) ? WaveguideCellState : FreeCellState;
            }
        }
        if (lifted.Count > 0)
            WaveguideVersion++;
        return lifted;
    }

    /// <summary>True when a breakout reservation currently blocks the cell.</summary>
    public bool IsBreakoutReservedCell(int gridX, int gridY) =>
        IsInBounds(gridX, gridY) && _cells[gridX, gridY] == BreakoutReservationCellState;

    /// <summary>
    /// True when the cell is unusable for a breakout of pins owned by <paramref name="ownComponents"/>:
    /// out of bounds, a frozen group path, or the body or clearance band of any other
    /// component. The own components' cells and waveguides do not obstruct a plan.
    /// </summary>
    internal bool ObstructsBreakout(int gridX, int gridY, IReadOnlySet<Component> ownComponents)
    {
        if (!IsInBounds(gridX, gridY)) return true;
        byte state = _cells[gridX, gridY];
        if (state == 3) return true;
        if (state != 1) return false;
        return _componentOwnership.HasForeignClaim(gridX, gridY, ownComponents);
    }

    /// <summary>
    /// The state a waveguide cell falls back to when its waveguide leaves: reserved when a
    /// breakout reservation covers it, free otherwise.
    /// </summary>
    private byte FreedCellState(int gridX, int gridY)
    {
        lock (_breakoutLock)
        {
            return _breakoutReservationRefs.ContainsKey((gridX, gridY))
                ? BreakoutReservationCellState
                : FreeCellState;
        }
    }

    /// <summary>
    /// The cells of <paramref name="cells"/> a registered waveguide covers. A waveguide
    /// registered over a reservation leaves the cell reserved; once the reservation goes, the
    /// cell must read as that waveguide again — never as free.
    /// </summary>
    private HashSet<(int x, int y)> CellsCoveredByWaveguides(HashSet<(int x, int y)> cells)
    {
        if (cells.Count == 0) return new HashSet<(int x, int y)>();
        lock (_waveguideCellsLock)
            return _waveguideCells.Values.SelectMany(c => c.Where(cells.Contains)).ToHashSet();
    }

    /// <summary>Releases reserved cells: a cell a registered waveguide still covers becomes a waveguide cell.</summary>
    private void ReleaseReservedCells(List<(int x, int y)> released)
    {
        if (released.Count == 0) return;
        var covered = CellsCoveredByWaveguides(released.ToHashSet());
        foreach (var (x, y) in released)
        {
            if (_cells[x, y] != BreakoutReservationCellState) continue;
            _cells[x, y] = covered.Contains((x, y)) ? WaveguideCellState : FreeCellState;
        }
    }

    /// <summary>Forgets all reservation bookkeeping (the cell array was wiped by a rebuild).</summary>
    private void ResetBreakoutReservationBookkeeping()
    {
        lock (_breakoutLock)
        {
            _breakoutReservations.Clear();
            _breakoutReservationRefs.Clear();
        }
    }
}
