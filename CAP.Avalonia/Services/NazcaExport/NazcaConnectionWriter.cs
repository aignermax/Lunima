using System.Text;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Export;
using CAP_Core.Export.InterconnectRouting;
using CAP_Core.Routing;
using CAP_Core.Routing.InterconnectRouting;
using CAP_Core.Routing.MetalRouting;
using static CAP.Avalonia.Services.NazcaExport.NazcaConnectionFallbackWriter;
using static CAP.Avalonia.Services.NazcaExport.NazcaCrossingWriter;
using static CAP.Avalonia.Services.NazcaExport.NazcaFrozenPathWriter;
using static CAP.Avalonia.Services.NazcaExport.NazcaSegmentStyle;
using static CAP.Avalonia.Services.NazcaExport.NazcaSegmentWriter;

namespace CAP.Avalonia.Services.NazcaExport;

/// <summary>Dispatches live connections by geometry, material, routing style, and process cross-section.</summary>
internal static class NazcaConnectionWriter
{
    internal static void AppendConnections(
        StringBuilder sb,
        DesignCanvasViewModel canvas,
        Dictionary<Component, string> componentNames,
        MetalRoutingSpec metalSpec,
        int? gdsLayer = null,
        List<string>? skippedConnections = null,
        List<string>? unresolvedCrossings = null,
        NazcaProcessInterconnectPlan? interconnectPlan = null,
        bool forceProcessCrossSections = false,
        SiepicWaveguideCellWriter? waveguideCells = null)
    {
        var hasFrozenPaths = canvas.Components.Any(vm => vm.Component is ComponentGroup)
            || canvas.CanvasFrozenPaths.Count > 0;
        if (canvas.Connections.Count == 0 && !hasFrozenPaths)
            return;

        sb.AppendLine("        # Waveguide Connections");

        var metalStyle = metalSpec.ToTraceStyle();
        var metalConnections = new List<WaveguideConnection>();
        var unresolvedCrossingCandidates = new List<WaveguideConnection>();
        foreach (var connVm in canvas.Connections)
        {
            var conn = connVm.Connection;
            // Skip connections that touch a virtual analysis tool — those pins
            // have no physical fab counterpart.
            if (conn.StartPin?.ParentComponent?.IsAnalysisTool == true) continue;
            if (conn.EndPin?.ParentComponent?.IsAnalysisTool == true) continue;

            // A cross-chiplet facet link couples free space between two separate dies —
            // its transmission is the edge-coupler offset × gap model, not waveguide
            // geometry. Emitting it would draw a waveguide across the gap between the
            // chiplets, so it is noted as a comment instead (the export stays verifiable).
            if (conn.IsCrossChipletFacetLink)
            {
                sb.AppendLine("        # Cross-chiplet facet link (free-space edge coupling) - no waveguide exported");
                continue;
            }

            // A placeholder (self-crossing fallback with no optical model) or invalid
            // (bend radius violation) route must never render as geometry — the design
            // still exports, just without this connection's geometry. A missing route is
            // NOT skipped: it falls back to the pin-to-pin straight below, same as before.
            if (ExportableConnections.TryRecordSkip(conn.RoutedPath, conn.StartPin, conn.EndPin, skippedConnections))
                continue;

            // An untouched imported route writes back exactly the polygons it was read from.
            if (conn.AsDrawnGeometry is { } asDrawn)
            {
                AsDrawnNazcaWriter.Append(sb, asDrawn);
                continue;
            }

            // Electrical connections are metal traces, not optical waveguides — emit them on
            // the process metal layer/width instead of the waveguide layer (issue #682). A
            // connection is metal only when BOTH pins are electrical; a mixed optical+electrical
            // or all-optical connection stays a waveguide (issue #686 review — the earlier
            // "either pin" predicate would draw a mixed connection wholly on the metal layer,
            // silently dropping the optical waveguide). Metal connections are remembered so
            // bridge markers can be placed where they cross optical paths (below).
            var metal = IsMetalConnection(conn.StartPin, conn.EndPin) ? metalStyle : null;
            if (metal != null)
                metalConnections.Add(conn);
            else if (conn.IsBlockedFallback)
                // Real (non-placeholder) geometry that WaveguideConnectionManager's sibling-
                // crossing pass still flagged — it renders (below), but the layout deserves a
                // second look unless a bridge marker actually resolves the crossing.
                unresolvedCrossingCandidates.Add(conn);

            // The import source's (layer, datatype) tag (route-derived GDS connections
            // with an unambiguous source layer): the connection exports on the ORIGINAL
            // layer — for a metal trace the tag wins over the process metal default.
            var sourceLayer = conn.SourceGdsLayer is int sourceL && conn.SourceGdsDataType is int sourceD
                ? (sourceL, sourceD)
                : ((int Layer, int DataType)?)null;

            // On a multi-process canvas the endpoint pins' PDK stamps decide which
            // process cross-section this connection routes on (width/layer per segment,
            // its own interconnect for the pin-to-pin fallback). A single-process canvas
            // keeps the default cross-section so its export stays byte-identical to the
            // legacy global-interconnect output — except an EBeam-only design
            // (forceProcessCrossSections), whose segments must land on the EBeam layer.
            var crossSection = interconnectPlan?.UsesPerProcessCrossSections == true || forceProcessCrossSections
                ? ConnectionCrossSectionResolver.Resolve(conn.StartPin, conn.EndPin)
                : default;

            // Process companion layers (cladding / trench / keep-out bands) follow every
            // routed optical waveguide; imported geometry carries its own and metal has none.
            var companionXs = metal == null && sourceLayer == null
                ? interconnectPlan?.Companions.XsectionFor(conn.StartPin, conn.EndPin, conn.WidthMicrometers)
                : null;

            // Explicit routing style (issue #574) applies to OPTICAL waveguides only:
            // point-to-point styles export a single Nazca primitive (strt/sinebend/cobra)
            // on the waveguide layer instead of the routed segments; Bend and Euler return
            // null here and fall through to AppendSegmentExport below, which writes their
            // exact canvas stub–arc–stub segments (a lone nd.bend/nd.euler cannot land on
            // an arbitrary end pin). An electrical connection must stay a metal trace
            // (issue #682) — never emit it as an optical primitive even if a style was
            // set, so styled export is gated on metal == null.
            if (metal == null)
            {
                var styledLine = NazcaConnectionStyleWriter.Format(conn, crossSection.GdsLayer ?? gdsLayer, sourceLayer);
                if (styledLine != null)
                {
                    sb.AppendLine(styledLine);
                    if (companionXs != null)
                        sb.AppendLine(NazcaConnectionStyleWriter.Format(conn, xsectionName: companionXs));
                    continue;
                }
            }

            // Routed connections export their real segments; only routeless
            // connections fall back to a p2p interconnect. An EBeam-only export
            // wraps each routed optical connection in its own SiEPIC waveguide
            // cell (openEBL gap #4) instead of flattening it into the top cell.
            var segments = conn.GetPathSegments();

            if (segments.Count > 0)
            {
                if (metal == null && waveguideCells != null)
                {
                    var cellContent = new StringBuilder();
                    AppendSegmentExport(cellContent, segments, conn.StartPin, conn.EndPin, metal, sourceLayer, crossSection, companionXs);
                    waveguideCells.AppendWaveguideCell(sb, cellContent, segments, conn.StartPin, conn.EndPin);
                }
                else
                {
                    AppendSegmentExport(sb, segments, conn.StartPin, conn.EndPin, metal, sourceLayer, crossSection, companionXs);
                }
            }
            else
                AppendFallbackExport(sb, conn.StartPin, conn.EndPin, componentNames, metal, sourceLayer,
                    interconnectPlan?.InterconnectFor(conn) ?? "ic");
        }

        // Export frozen waveguide paths from ComponentGroups
        foreach (var compVm in canvas.Components)
        {
            if (compVm.Component is ComponentGroup group)
                AppendGroupFrozenPaths(sb, group, metalStyle, componentNames, skippedConnections, interconnectPlan, forceProcessCrossSections, waveguideCells);
        }

        // Canvas-level pin-less frozen paths (issue #856): imported route geometry
        // released by ungrouping exports exactly like it did inside the group.
        foreach (var pathVm in canvas.CanvasFrozenPaths)
            AppendFrozenPath(sb, pathVm.Path, metalStyle, componentNames, skippedConnections, interconnectPlan, forceProcessCrossSections, waveguideCells);

        AppendBridgeMarkers(sb, canvas, metalConnections, metalSpec);
        CollectUnresolvedCrossings(unresolvedCrossingCandidates, metalConnections, metalSpec, unresolvedCrossings);

        sb.AppendLine();
    }
}
