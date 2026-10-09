using CAP.Avalonia.Services.Localization;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;

namespace CAP.Avalonia.ViewModels.Canvas.RerouteImported;

public partial class RerouteImportedRoutesViewModel
{
    /// <summary>The imported state of a re-route target, to fall back to when the router finds no path.</summary>
    private sealed record DrawnRouteSnapshot(
        WaveguideConnectionViewModel Target, RoutedPath Path, AsDrawnGeometry? AsDrawn);

    private static List<DrawnRouteSnapshot> SnapshotDrawnRoutes(IEnumerable<WaveguideConnectionViewModel> targets) =>
        targets
            .Where(t => t.Connection.RoutedPath is not null)
            .Select(t => new DrawnRouteSnapshot(t, t.Connection.RoutedPath!.DeepCopy(), t.Connection.AsDrawnGeometry))
            .ToList();

    /// <summary>
    /// Puts the imported route back on every target the router could only connect with a
    /// blocked fallback (no free path through the frozen layout around it): a dashed
    /// placeholder would replace a working drawn waveguide with a broken one. The route is
    /// frozen again with its drawn polygons, so it also stops costing time in later passes.
    /// </summary>
    /// <returns>How many targets kept their drawn route.</returns>
    private static int KeepDrawnRouteWhereBlocked(IEnumerable<DrawnRouteSnapshot> snapshots)
    {
        int kept = 0;
        foreach (var snapshot in snapshots)
        {
            var connection = snapshot.Target.Connection;
            if (!connection.IsBlockedFallback && connection.RoutedPath is not null)
                continue;
            connection.RestoreCachedPath(snapshot.Path.DeepCopy());
            connection.IsRouteFrozen = true;
            if (snapshot.AsDrawn is { } asDrawn)
                connection.AttachAsDrawnGeometry(asDrawn);
            snapshot.Target.NotifyPathChanged();
            kept++;
        }
        return kept;
    }

    /// <summary>Appends the "kept N drawn routes" note to a result line when some routes were kept.</summary>
    private static string WithKeptNote(string result, int kept) =>
        kept == 0
            ? result
            : result + " — " + string.Format(
                LocalizationService.Instance.Translate("Routing.Reroute.KeptNoPath"), kept);
}
