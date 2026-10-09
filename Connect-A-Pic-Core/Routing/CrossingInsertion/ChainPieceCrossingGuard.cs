using CAP_Core.Components.Connections;

namespace CAP_Core.Routing.CrossingInsertion;

/// <summary>
/// Geometric check of a crossing chain before it is applied: no piece may cross another
/// wire except through the placed crossings. The grid search alone cannot guarantee it —
/// pin corridors are cleared for the route's own pins, so a leg can cut through the
/// fan-out of a neighbouring wire leaving an adjacent pin.
/// </summary>
internal static class ChainPieceCrossingGuard
{
    /// <summary>
    /// True when any of the <paramref name="pieces"/> properly crosses another piece or a
    /// connection of <paramref name="manager"/> that stays in the design.
    /// </summary>
    /// <param name="pieces">The chain's new connections with the paths they will carry.</param>
    /// <param name="removed">Connections the chain replaces (not checked against).</param>
    /// <param name="manager">The connection manager holding the design's other wires.</param>
    public static bool CrossesAnotherWire(
        IReadOnlyList<(WaveguideConnection Connection, RoutedPath Path)> pieces,
        IReadOnlyCollection<WaveguideConnection> removed, WaveguideConnectionManager manager)
    {
        var sampledPieces = pieces.Select(p => (p.Connection, Line: SampledPolyline.From(p.Path))).ToList();
        var others = manager.Connections
            .Where(c => !removed.Contains(c) && !c.IsBlockedFallback && c.RoutedPath?.Segments is { Count: > 0 })
            .Select(c => (Connection: c, Line: SampledPolyline.From(c.RoutedPath!)))
            .ToList();

        for (int i = 0; i < sampledPieces.Count; i++)
        {
            var piece = sampledPieces[i];
            if (others.Any(other => Crosses(piece, other)))
                return true;
            if (sampledPieces.Skip(i + 1).Any(other => Crosses(piece, other)))
                return true;
        }
        return false;
    }

    private static bool Crosses((WaveguideConnection Connection, SampledPolyline Line) a,
                                (WaveguideConnection Connection, SampledPolyline Line) b) =>
        !SharesPin(a.Connection, b.Connection) && a.Line.Crosses(b.Line);

    private static bool SharesPin(WaveguideConnection a, WaveguideConnection b) =>
        ReferenceEquals(a.StartPin, b.StartPin) || ReferenceEquals(a.StartPin, b.EndPin)
        || ReferenceEquals(a.EndPin, b.StartPin) || ReferenceEquals(a.EndPin, b.EndPin);
}
