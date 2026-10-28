using CAP_Core.Components.Core;

namespace CAP_Core.Routing.AStarPathfinder;

public partial class PathfindingGrid
{
    private readonly ComponentObstacleOwnershipIndex _componentOwnership = new();

    /// <summary>Returns registered corridor cells for the given endpoint pins.</summary>
    public HashSet<(int x, int y)> GetPinCorridorCells(IEnumerable<PhysicalPin> pins) =>
        _componentOwnership.GetPinCorridorCells(pins);

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
        return _componentOwnership.BlocksDespiteCorridorTolerance(gridX, gridY, toleratedCorridorCells);
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
        return _componentOwnership.BlocksDespiteCorridorTolerance(gridX, gridY, toleratedCorridorCells);
    }

    /// <summary>
    /// True when the cell is blocked only by components' padding bands (their routing
    /// clearance), with no component body claiming it.
    /// </summary>
    internal bool IsComponentPaddingOnly(int gridX, int gridY)
    {
        if (!IsInBounds(gridX, gridY) || _cells[gridX, gridY] != 1) return false;
        return _componentOwnership.HasPaddingClaimsOnly(gridX, gridY);
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
                if (_componentOwnership.HasForeignBodyClaim(gx, gy, pin.ParentComponent))
                    sawForeignBody = true;
            }
            if (laneFree)
                return false;
        }
        return sawForeignBody;
    }

}
