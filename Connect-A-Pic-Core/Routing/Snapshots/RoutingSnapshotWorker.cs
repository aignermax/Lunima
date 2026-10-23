namespace CAP_Core.Routing.Snapshots;

/// <summary>Owns one isolated calculation, publishing only value frames and a completed ownership transfer.</summary>
public static class RoutingSnapshotWorker
{
    public static RoutingPassResult? Run(RoutingInputSnapshot input, RoutingProgressMailbox progress,
        CancellationToken cancellationToken, bool calculateRoutes = true)
    {
        if (cancellationToken.IsCancellationRequested) return null;
        var graph = input.CreatePrivateGraph();
        var componentIds = graph.Components.Select(component => component.Id).ToHashSet();
        var modelOrigins = RoutingPassResult.CaptureModelOrigins(graph.Manager);
        var originalPaths = RoutingPassResult.AllWires(graph.Manager).ToDictionary(wire => wire.Id,
            wire => (Path: wire.RoutedPath, Geometry: RouteGeometryValue.Capture(wire.RoutedPath)));
        progress.Initialize(graph.Manager.Connections);
        if (calculateRoutes)
        {
            graph.Manager.OnRoutingPhaseChanged = progress.PhaseChanged;
            graph.Manager.OnComplexRouteStarted = progress.ComplexStarted;
            graph.Manager.RecalculateAllTransmissions(() => progress.Completed(graph), cancellationToken);
        }
        if (cancellationToken.IsCancellationRequested) return null;
        graph.Manager.OnRoutingPhaseChanged = null;
        graph.Manager.OnComplexRouteStarted = null;
        graph.Router.OnComplexRouteStarted = null;
        var unchangedPaths = RoutingPassResult.AllWires(graph.Manager).Where(wire =>
            originalPaths.TryGetValue(wire.Id, out var original) && ReferenceEquals(original.Path, wire.RoutedPath)
            && (original.Geometry?.Matches(wire.RoutedPath) ?? wire.RoutedPath == null)).Select(wire => wire.Id);
        return RoutingPassResult.Capture(graph, componentIds, modelOrigins, calculateRoutes, unchangedPaths);
    }
}
