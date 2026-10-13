using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.CrossingInsertion;

/// <summary>
/// Second chance for a crossing chain taken out because its wire went stale: when the wire
/// could not be routed without crossings, the old chain goes back and only the pieces whose
/// pin moved are routed again. The chain stays only if every piece routes cleanly, no piece
/// crosses another wire and no other wire runs over one of its crossings — otherwise it is
/// taken out again exactly, and the regular crossing search takes over. Reusing the chain
/// keeps the crossings the wire already had instead of searching a fresh, often longer one,
/// and leaves every other wire alone.
/// </summary>
internal static class DissolvedChainRepair
{
    /// <summary>How far (µm) a crossing body is shrunk before testing other wires against it, so a wire touching it at a port does not count.</summary>
    private const double BodyInsetMicrometers = 0.5;

    /// <summary>Largest gap (µm) between a piece's route end and its pin that still counts as attached.</summary>
    private const double AttachedToleranceMicrometers = 1.0;

    /// <summary>
    /// Puts <paramref name="chain"/> back and re-routes its detached pieces. True when the
    /// chain fits and stays; false with the design unchanged otherwise.
    /// </summary>
    public static bool TryRestore(
        DissolvedChain chain, WaveguideConnectionManager manager, WaveguideRouter router, CancellationToken cancellationToken)
    {
        var grid = router.PathfindingGrid;
        if (grid == null || !chain.Merged.IsBlockedFallback || !chain.Added.All(manager.Connections.Contains))
            return false;

        chain.Undo();
        var pieces = Pieces(chain.Crossings, manager);
        var saved = pieces.ToDictionary(p => p, p => p.RoutedPath);
        if (RerouteDetached(pieces, manager, router, cancellationToken)
            && !ChainPieceCrossingGuard.CrossesAnotherWire(pieces.Select(p => (p, p.RoutedPath!)).ToList(), pieces, manager)
            && !RunsOverCrossings(chain.Crossings, manager))
            return true;

        foreach (var (piece, path) in saved)
        {
            grid.RemoveWaveguideObstacle(piece.Id);
            if (path != null) piece.RestoreCachedPath(path);
        }
        chain.Redo();
        // The crossing pass keeps blocked placeholders off the grid while it works.
        grid.RemoveWaveguideObstacle(chain.Merged.Id);
        return false;
    }

    private static List<WaveguideConnection> Pieces(IReadOnlyList<Component> crossings, WaveguideConnectionManager manager) =>
        manager.Connections
            .Where(c => crossings.Contains(c.StartPin.ParentComponent) || crossings.Contains(c.EndPin.ParentComponent))
            .ToList();

    /// <summary>Routes every piece whose route no longer ends at its pins; false when one stays blocked.</summary>
    private static bool RerouteDetached(
        List<WaveguideConnection> pieces, WaveguideConnectionManager manager, WaveguideRouter router,
        CancellationToken cancellationToken)
    {
        var grid = router.PathfindingGrid!;
        foreach (var piece in pieces.Where(p => !IsAttached(p)))
        {
            grid.RemoveWaveguideObstacle(piece.Id);
            piece.RecalculateTransmission(router, cancellationToken: cancellationToken);
            if (!piece.IsPathValid || piece.RoutedPath is not { IsBlockedFallback: false } path || cancellationToken.IsCancellationRequested)
                return false;
            grid.AddWaveguideObstacle(piece.Id, path.Segments, manager.WaveguideWidthMicrometers);
        }
        return true;
    }

    private static bool IsAttached(WaveguideConnection piece)
    {
        var path = piece.RoutedPath;
        if (path == null || path.Segments.Count == 0) return false;
        return Near(piece.StartPin, path.Segments[0].StartPoint) && Near(piece.EndPin, path.Segments[^1].EndPoint);
    }

    private static bool Near(PhysicalPin pin, (double X, double Y) point)
    {
        var (x, y) = pin.GetAbsolutePosition();
        return Math.Abs(x - point.X) <= AttachedToleranceMicrometers && Math.Abs(y - point.Y) <= AttachedToleranceMicrometers;
    }

    /// <summary>
    /// True when a wire not docked at a crossing runs through its body — the wires routed
    /// while the chain was out did not see the crossings.
    /// </summary>
    private static bool RunsOverCrossings(IReadOnlyList<Component> crossings, WaveguideConnectionManager manager)
    {
        foreach (var crossing in crossings)
        {
            var body = Body(crossing);
            foreach (var wire in manager.Connections)
            {
                if (wire.StartPin.ParentComponent == crossing || wire.EndPin.ParentComponent == crossing) continue;
                if (wire.IsBlockedFallback || wire.RoutedPath is not { Segments.Count: > 0 } path) continue;
                if (Enters(PathIntersectionDetector.SamplePolyline(path), body)) return true;
            }
        }
        return false;
    }

    /// <summary>The crossing body: the box spanned by its ports, shrunk by <see cref="BodyInsetMicrometers"/>.</summary>
    private static (double MinX, double MinY, double MaxX, double MaxY) Body(Component crossing)
    {
        var ports = crossing.PhysicalPins.Select(p => p.GetAbsolutePosition()).ToList();
        return (ports.Min(p => p.x) + BodyInsetMicrometers, ports.Min(p => p.y) + BodyInsetMicrometers,
                ports.Max(p => p.x) - BodyInsetMicrometers, ports.Max(p => p.y) - BodyInsetMicrometers);
    }

    private static bool Enters(List<(double X, double Y)> line, (double MinX, double MinY, double MaxX, double MaxY) box)
    {
        for (int i = 0; i + 1 < line.Count; i++)
        {
            if (SegmentHitsBox(line[i], line[i + 1], box)) return true;
        }
        return false;
    }

    /// <summary>Liang–Barsky clip: true when the segment has a part inside the box.</summary>
    private static bool SegmentHitsBox((double X, double Y) a, (double X, double Y) b, (double MinX, double MinY, double MaxX, double MaxY) box)
    {
        double t0 = 0, t1 = 1, dx = b.X - a.X, dy = b.Y - a.Y;
        foreach (var (p, q) in new[] { (-dx, a.X - box.MinX), (dx, box.MaxX - a.X), (-dy, a.Y - box.MinY), (dy, box.MaxY - a.Y) })
        {
            if (p == 0)
            {
                if (q < 0) return false;
                continue;
            }
            double t = q / p;
            if (p < 0) t0 = Math.Max(t0, t);
            else t1 = Math.Min(t1, t);
            if (t0 > t1) return false;
        }
        return true;
    }
}
