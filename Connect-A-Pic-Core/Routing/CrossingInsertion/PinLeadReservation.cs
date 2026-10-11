using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing.AStarPathfinder;

namespace CAP_Core.Routing.CrossingInsertion;

/// <summary>
/// Keeps a lifted pin neighbour's way out open while the blocked wire routes first: a short
/// virtual lead straight out of the neighbour's pin is registered as a waveguide obstacle, so
/// the first wire cannot turn across the neighbour's lane right at the pins (the grid and the
/// corridor check both respect it). The leads are released before the neighbours route.
/// </summary>
internal sealed class PinLeadReservation
{
    /// <summary>
    /// Length (µm) of a reserved lead: room for the neighbour's own first bend plus the
    /// wire beside it, so the first wire turns outside the neighbour instead of across it.
    /// </summary>
    public const double LeadLengthMicrometers = 30.0;

    private readonly PathfindingGrid _grid;
    private readonly List<Guid> _leads = new();

    private PinLeadReservation(PathfindingGrid grid) => _grid = grid;

    /// <summary>
    /// Reserves the leads of the neighbours' pins that sit next to one of the blocked wire's pins.
    /// </summary>
    /// <param name="grid">The routing grid.</param>
    /// <param name="neighbours">The lifted neighbours.</param>
    /// <param name="blocked">The wire that routes first.</param>
    /// <param name="isNear">Whether two pin positions count as neighbouring.</param>
    /// <param name="widthMicrometers">Waveguide width the leads block.</param>
    public static PinLeadReservation Reserve(
        PathfindingGrid grid, IEnumerable<WaveguideConnection> neighbours, WaveguideConnection blocked,
        Func<(double X, double Y), (double X, double Y), bool> isNear, double widthMicrometers)
    {
        var reservation = new PinLeadReservation(grid);
        var blockedPins = new[] { blocked.StartPin, blocked.EndPin }.Select(p => p.GetAbsolutePosition()).ToArray();
        foreach (var pin in neighbours.SelectMany(n => new[] { n.StartPin, n.EndPin }))
        {
            var position = pin.GetAbsolutePosition();
            if (!blockedPins.Any(b => isNear(position, b))) continue;
            reservation.AddLead(pin, widthMicrometers);
        }
        return reservation;
    }

    private void AddLead(PhysicalPin pin, double widthMicrometers)
    {
        var (x, y) = pin.GetAbsolutePosition();
        double angle = pin.GetAbsoluteAngle() * Math.PI / 180.0;
        var lead = new StraightSegment(x, y, x + Math.Cos(angle) * LeadLengthMicrometers,
                                       y + Math.Sin(angle) * LeadLengthMicrometers, pin.GetAbsoluteAngle());
        var id = Guid.NewGuid();
        _grid.AddWaveguideObstacle(id, new PathSegment[] { lead }, widthMicrometers);
        _leads.Add(id);
    }

    /// <summary>Removes the reserved leads from the grid.</summary>
    public void Release()
    {
        foreach (var id in _leads)
            _grid.RemoveWaveguideObstacle(id);
        _leads.Clear();
    }
}
