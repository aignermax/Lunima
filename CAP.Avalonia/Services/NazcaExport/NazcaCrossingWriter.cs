using System.Text;
using CAP.Avalonia.Services.MetalRouting;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Export;
using CAP_Core.Routing;
using CAP_Core.Routing.MetalRouting;
using static CAP.Avalonia.Services.NazcaExport.NazcaSegmentStyle;

namespace CAP.Avalonia.Services.NazcaExport;

/// <summary>Emits electrical bridge markers and reports optical crossings that remain unresolved.</summary>
internal static class NazcaCrossingWriter
{
    /// <summary>
    /// Reports the flagged connections a bridge marker does NOT resolve: a crossing is
    /// bridge-resolved only when it is a metal↔optical pair under
    /// <see cref="ElectricalCrossingPolicy.BridgeRequired"/> — exactly the condition
    /// <see cref="AppendBridgeMarkers"/> uses to decide whether to draw a marker at all, so a
    /// candidate that crosses no exported metal trace (an optical×optical crossing, or any
    /// crossing under a policy that never draws a marker) is genuinely unresolved.
    /// </summary>
    internal static void CollectUnresolvedCrossings(
        IReadOnlyList<WaveguideConnection> candidates,
        IReadOnlyList<WaveguideConnection> metalConnections,
        MetalRoutingSpec metalSpec,
        List<string>? unresolvedCrossings)
    {
        if (unresolvedCrossings == null || candidates.Count == 0)
            return;

        var bridgesCrossings = metalSpec.CrossingPolicy == ElectricalCrossingPolicy.BridgeRequired;
        foreach (var candidate in candidates)
        {
            bool resolvedByBridge = bridgesCrossings && candidate.RoutedPath != null
                && metalConnections.Any(metalConn =>
                    metalConn.RoutedPath != null
                    && PathIntersectionDetector.Crosses(candidate.RoutedPath, metalConn.RoutedPath));
            if (!resolvedByBridge)
                unresolvedCrossings.Add(ExportableConnections.Describe(candidate.StartPin, candidate.EndPin));
        }
    }

    /// <summary>
    /// Emits bridge markers for electrical metal traces (issue #682): when the active
    /// process requires bridges, a marker is placed wherever a metal trace crosses an
    /// optical waveguide path. The trace geometry itself is emitted inline by the
    /// connection loop above (on the process metal layer).
    /// </summary>
    internal static void AppendBridgeMarkers(
        StringBuilder sb,
        DesignCanvasViewModel canvas,
        IReadOnlyList<WaveguideConnection> metalConnections,
        MetalRoutingSpec metalSpec)
    {
        if (metalSpec.CrossingPolicy != ElectricalCrossingPolicy.BridgeRequired
            || metalConnections.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("        # Electrical bridge markers (metal over waveguide)");

        var opticalPaths = CollectOpticalPaths(canvas);
        foreach (var conn in metalConnections)
        {
            var segments = conn.GetPathSegments();
            if (segments.Count == 0)
                continue;

            var crossings = WaveguideCrossingDetector.FindCrossings(segments, opticalPaths);
            NazcaMetalTraceWriter.AppendBridges(sb, crossings, metalSpec);
        }
    }

    /// <summary>
    /// Collects the routed segment lists of all optical connections and frozen group
    /// paths — the geometry a metal trace can cross and that bridges must span.
    /// </summary>
    private static List<IReadOnlyList<PathSegment>> CollectOpticalPaths(DesignCanvasViewModel canvas)
    {
        var paths = new List<IReadOnlyList<PathSegment>>();
        foreach (var connVm in canvas.Connections)
        {
            var conn = connVm.Connection;
            // Both-pins-electrical connections render as metal; everything else
            // (including mixed pairs, which stay waveguides) is crossable geometry.
            if (IsMetalConnection(conn.StartPin, conn.EndPin)) continue;
            if (conn.StartPin?.ParentComponent?.IsAnalysisTool == true) continue;
            if (conn.EndPin?.ParentComponent?.IsAnalysisTool == true) continue;
            // A placeholder/invalid route never reaches export — it must not count as
            // crossable geometry either, or a metal trace would get a bridge marker over
            // a waveguide that isn't actually drawn.
            if (!conn.IsExportable()) continue;
            // A cross-chiplet facet link is not drawn (free-space coupling), so it must
            // not count as crossable geometry either.
            if (conn.IsCrossChipletFacetLink) continue;
            var segments = conn.GetPathSegments();
            if (segments.Count > 0)
                paths.Add(segments);
        }

        foreach (var compVm in canvas.Components)
        {
            if (compVm.Component is ComponentGroup group)
                CollectGroupFrozenPaths(group, paths);
        }

        foreach (var pathVm in canvas.CanvasFrozenPaths)
        {
            if (pathVm.Path.Path?.Segments?.Count > 0 && pathVm.Path.Path.IsExportable())
                paths.Add(pathVm.Path.Path.Segments);
        }
        return paths;
    }

    /// <summary>Adds all frozen waveguide paths of a group (and nested groups) to the list.</summary>
    private static void CollectGroupFrozenPaths(ComponentGroup group, List<IReadOnlyList<PathSegment>> paths)
    {
        foreach (var frozenPath in group.InternalPaths)
        {
            if (frozenPath?.Path?.Segments?.Count > 0 && frozenPath.Path.IsExportable())
                paths.Add(frozenPath.Path.Segments);
        }
        foreach (var child in group.ChildComponents)
        {
            if (child is ComponentGroup nested)
                CollectGroupFrozenPaths(nested, paths);
        }
    }
}
