using CAP_Core.Components.Connections;
using CAP_Core.Components.PinKinds;
using CAP_Core.Routing;
using CAP_Core.Routing.LengthEqualization;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>
/// Switching several connections back to Auto: when they form a bundle of facing pins, all of
/// them are laid out at once as planar arches — instead of the general router searching for one
/// wire at a time, which on a dense chip takes minutes and can fail on pin pairs a few µm apart.
/// Connections that share one length target keep it: the bundle is laid out at that length.
/// </summary>
internal static class BundleAutoRouting
{
    /// <summary>Fewer connections than this are left to the general router.</summary>
    public const int MinimumConnections = 2;

    /// <summary>Largest arch radius tried; smaller ones follow where it does not fit.</summary>
    private const double MaxRadiusMicrometers = 250.0;
    private const double ToleranceMicrometers = 0.1;
    private const double SameTargetMicrometers = 1e-3;
    private const int TargetDecimals = 3;

    /// <summary>
    /// The laid-out route of every connection in the bundle (the others are left to the general
    /// router), or null when there is no bundle or it does not fit cleanly. The design is read on
    /// the calling thread; the search runs on a background thread.
    /// </summary>
    public static async Task<IReadOnlyDictionary<WaveguideConnection, RoutedPath>?> TryLayOutAsync(
        DesignCanvasViewModel canvas, IReadOnlyList<WaveguideConnectionViewModel> targets)
    {
        var connections = targets.Select(t => t.Connection).Where(IsOptical).Distinct().ToList();
        if (connections.Count < MinimumConnections)
            return null;
        var shortest = Prepare(canvas, connections, null);
        double? common = MostCommonTarget(connections);
        var atTarget = common == null ? null : Prepare(canvas, connections, common);
        var result = await Task.Run(() => Solve(shortest, atTarget, common));
        return result?.Outcomes.Where(o => o.IsSuccess).ToDictionary(o => o.Connection, o => o.Path!);
    }

    /// <summary>
    /// Shortest shapes, unless every bundle wire shares one length target: then the bundle is laid
    /// out at that length so the constraint still holds.
    /// </summary>
    private static LengthEqualizationResult? Solve(LengthEqualizationPlan shortest, LengthEqualizationPlan? atTarget, double? common)
    {
        var result = LengthEqualizer.LayOutBundle(shortest);
        if (result == null || atTarget == null)
            return result;
        bool shared = result.Outcomes.Where(o => o.IsSuccess)
            .All(o => o.Connection.TargetLengthMicrometers is { } t && Math.Abs(t - common!.Value) <= SameTargetMicrometers);
        return shared ? LengthEqualizer.LayOutBundle(atTarget) ?? result : result;
    }

    private static LengthEqualizationPlan Prepare(DesignCanvasViewModel canvas, List<WaveguideConnection> connections, double? target) =>
        new LengthEqualizer(canvas.Router).Prepare(connections, canvas.ConnectionManager.Connections.ToList(),
            canvas.Components.Select(c => c.Component).ToList(), target, MaxRadiusMicrometers, ToleranceMicrometers,
            rerouteBundle: true);

    /// <summary>The length target most of the connections carry, or null when none has one.</summary>
    private static double? MostCommonTarget(IReadOnlyList<WaveguideConnection> connections) =>
        connections.Where(c => c.TargetLengthMicrometers != null)
            .GroupBy(c => Math.Round(c.TargetLengthMicrometers!.Value, TargetDecimals))
            .OrderByDescending(g => g.Count())
            .Select(g => g.First().TargetLengthMicrometers)
            .FirstOrDefault();

    private static bool IsOptical(WaveguideConnection connection) =>
        !PinKindHelper.IsElectrical(connection.StartPin) || !PinKindHelper.IsElectrical(connection.EndPin);
}
