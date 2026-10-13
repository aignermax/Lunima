using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.CrossingInsertion.CrossingMove;

/// <summary>
/// Plans moving a crossing along the axis of one of its two wires. The pieces of that wire
/// keep their routes and only stretch or shrink the straight that runs into the crossing —
/// valid exactly when that straight is collinear with the axis and keeps at least
/// <see cref="MinEndStraightMicrometers"/>. The pieces of the wire across the axis (and any
/// piece the patch does not fit) are re-routed. Pure geometry: nothing is mutated.
/// </summary>
public static class CrossingAxisMovePlanner
{
    /// <summary>Shortest straight (µm) a patched piece may keep in front of its crossing port.</summary>
    public const double MinEndStraightMicrometers = CrossingInsertionService.ChainClearanceMicrometers;

    /// <summary>Largest gap (µm) between a piece's end and its crossing port.</summary>
    private const double PortToleranceMicrometers = 0.5;

    /// <summary>Largest sideways offset (µm) of the moved end from the straight's line for it to stay collinear.</summary>
    private const double CollinearToleranceMicrometers = 0.01;

    private const double QuarterTurnDegrees = 45.0;
    private const double HalfTurnDegrees = 180.0;
    private const double ThreeQuarterTurnDegrees = 135.0;

    /// <summary>The translation moving by <paramref name="deltaMicrometers"/> along <paramref name="axis"/> means.</summary>
    public static (double X, double Y) DeltaFor(CrossingAxis axis, double deltaMicrometers) =>
        axis == CrossingAxis.Horizontal ? (deltaMicrometers, 0.0) : (0.0, deltaMicrometers);

    /// <summary>
    /// Plans the move of <paramref name="crossing"/> by <paramref name="deltaMicrometers"/>
    /// along <paramref name="axis"/> for the pieces among <paramref name="connections"/>.
    /// </summary>
    public static CrossingAxisMovePlan Plan(
        Component crossing, IEnumerable<WaveguideConnection> connections, CrossingAxis axis, double deltaMicrometers)
    {
        var delta = DeltaFor(axis, deltaMicrometers);
        var patched = new List<PatchedCrossingPiece>();
        var rerouted = new List<WaveguideConnection>();
        foreach (var piece in DockedPieces(crossing, connections))
        {
            var path = TryPatch(piece, crossing, axis, delta);
            if (path != null)
                patched.Add(new PatchedCrossingPiece(piece, path));
            else
                rerouted.Add(piece);
        }
        return new CrossingAxisMovePlan(crossing, delta, patched, rerouted);
    }

    /// <summary>The connections with an end on one of the crossing's ports.</summary>
    public static List<WaveguideConnection> DockedPieces(Component crossing, IEnumerable<WaveguideConnection> connections) =>
        connections.Where(c => c.StartPin.ParentComponent == crossing || c.EndPin.ParentComponent == crossing).ToList();

    /// <summary>
    /// The first wire (not docked on the crossing) whose geometry the crossing's footprint
    /// would cover after moving by <paramref name="delta"/>, or null when the spot is free.
    /// </summary>
    public static WaveguideConnection? FindWireUnderMovedFootprint(
        Component crossing, (double X, double Y) delta, IEnumerable<WaveguideConnection> connections)
    {
        double minX = crossing.PhysicalX + delta.X, minY = crossing.PhysicalY + delta.Y;
        double maxX = minX + crossing.WidthMicrometers, maxY = minY + crossing.HeightMicrometers;
        foreach (var connection in connections)
        {
            if (connection.StartPin.ParentComponent == crossing || connection.EndPin.ParentComponent == crossing)
                continue;
            foreach (var segment in connection.GetPathSegments())
            {
                var bounds = PathSegmentBounds.Of(segment);
                if (bounds.MinX < maxX && bounds.MaxX > minX && bounds.MinY < maxY && bounds.MaxY > minY)
                    return connection;
            }
        }
        return null;
    }

    /// <summary>The piece's route with its end straight following the port, or null when that does not fit.</summary>
    private static RoutedPath? TryPatch(WaveguideConnection piece, Component crossing, CrossingAxis axis, (double X, double Y) delta)
    {
        bool startDocked = piece.StartPin.ParentComponent == crossing;
        bool endDocked = piece.EndPin.ParentComponent == crossing;
        if (startDocked == endDocked) return null;
        var port = endDocked ? piece.EndPin : piece.StartPin;
        if (!RunsAlong(port, axis) || piece.RoutedPath is not { } route || route.Segments.Count == 0 || route.IsBlockedFallback)
            return null;

        var copy = route.DeepCopy();
        var straight = copy.Segments[endDocked ? ^1 : 0] as StraightSegment;
        if (straight == null) return null;
        var portEnd = endDocked ? straight.EndPoint : straight.StartPoint;
        var farEnd = endDocked ? straight.StartPoint : straight.EndPoint;
        if (!IsAtPort(portEnd, port)) return null;

        var moved = (X: portEnd.X + delta.X, Y: portEnd.Y + delta.Y);
        if (!KeepsDirection(farEnd, portEnd, moved)) return null;
        if (endDocked)
            straight.EndPoint = moved;
        else
            straight.StartPoint = moved;
        return copy;
    }

    /// <summary>
    /// True when the straight from <paramref name="farEnd"/> to <paramref name="portEnd"/>
    /// runs along the move and still points the same way with enough length after it.
    /// </summary>
    private static bool KeepsDirection((double X, double Y) farEnd, (double X, double Y) portEnd, (double X, double Y) moved)
    {
        double dx = portEnd.X - farEnd.X, dy = portEnd.Y - farEnd.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0) return false;
        var (ux, uy) = (dx / length, dy / length);
        double mx = moved.X - farEnd.X, my = moved.Y - farEnd.Y;
        double sideways = Math.Abs(mx * -uy + my * ux);
        double along = mx * ux + my * uy;
        return sideways <= CollinearToleranceMicrometers && along >= MinEndStraightMicrometers;
    }

    /// <summary>True when the wire through <paramref name="port"/> runs along <paramref name="axis"/>.</summary>
    private static bool RunsAlong(PhysicalPin port, CrossingAxis axis)
    {
        double angle = ((port.GetAbsoluteAngle() % HalfTurnDegrees) + HalfTurnDegrees) % HalfTurnDegrees;
        bool horizontal = angle < QuarterTurnDegrees || angle > ThreeQuarterTurnDegrees;
        return horizontal == (axis == CrossingAxis.Horizontal);
    }

    private static bool IsAtPort((double X, double Y) point, PhysicalPin port)
    {
        var (x, y) = port.GetAbsolutePosition();
        return Math.Abs(point.X - x) <= PortToleranceMicrometers && Math.Abs(point.Y - y) <= PortToleranceMicrometers;
    }
}
