using CAP_Core.Components.Connections;
using CAP_Core.Routing;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;

/// <summary>
/// The routed paths of every connection at one moment. Undo puts components and crossings
/// back, but the wires a trial re-routed would otherwise be routed again from scratch — and a
/// fresh joint routing pass need not reproduce the routes the design had (an example's routes
/// were baked). Restoring the paths makes the next pass keep them, because incremental routing
/// keeps every route whose endpoints still match.
/// </summary>
public sealed class RouteSnapshot
{
    private readonly List<Entry> _entries;

    private RouteSnapshot(List<Entry> entries) => _entries = entries;

    /// <summary>Captures the paths of all connections of the open design.</summary>
    public static RouteSnapshot Capture(AgentDesignSession session) =>
        new(session.Canvas.ConnectionManager.Connections
            .Select(c => new Entry(c, c.RoutedPath, c.IsRouteFrozen, new Dictionary<int, double>(c.StraightShiftOffsets)))
            .ToList());

    /// <summary>
    /// Puts the captured paths back on the connections that are on the canvas again; returns
    /// how many were restored. Connections that no longer exist are skipped.
    /// </summary>
    public int Restore(AgentDesignSession session)
    {
        var present = session.Canvas.ConnectionManager.Connections.ToHashSet();
        var restored = 0;
        foreach (var entry in _entries.Where(e => present.Contains(e.Connection) && e.Path != null))
        {
            if (!ReferenceEquals(entry.Connection.RoutedPath, entry.Path))
            {
                entry.Connection.RestoreCachedPath(entry.Path!);
                restored++;
            }
            entry.Connection.IsRouteFrozen = entry.Frozen;
            RestoreOffsets(entry);
        }
        foreach (var vm in session.Canvas.Connections)
            vm.NotifyPathChanged();
        return restored;
    }

    private static void RestoreOffsets(Entry entry)
    {
        var offsets = entry.Connection.StraightShiftOffsets;
        offsets.Clear();
        foreach (var (index, offset) in entry.Offsets)
            offsets[index] = offset;
    }

    private sealed record Entry(WaveguideConnection Connection, RoutedPath? Path, bool Frozen, Dictionary<int, double> Offsets);
}
