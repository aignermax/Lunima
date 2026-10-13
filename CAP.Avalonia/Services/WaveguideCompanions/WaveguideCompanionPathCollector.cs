using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Components.PinKinds;
using CAP_Core.Routing;

namespace CAP.Avalonia.Services.WaveguideCompanions;

/// <summary>
/// One routed optical waveguide that carries its process' companion bands.
/// </summary>
/// <param name="StartPin">Start pin (resolves the process).</param>
/// <param name="EndPin">End pin (resolves the process).</param>
/// <param name="CoreWidthMicrometers">Core width the bands grow beyond.</param>
/// <param name="Segments">Centerline segments in canvas coordinates.</param>
public sealed record WaveguideCompanionPath(
    PhysicalPin? StartPin, PhysicalPin? EndPin, double CoreWidthMicrometers, IReadOnlyList<PathSegment> Segments);

/// <summary>
/// Collects the canvas waveguides Lunima routed itself — top-level connections plus
/// pinned, untagged frozen paths (canvas-level and inside groups, recursively). Imported
/// geometry (drawn polygons, source-layer tags) is left out: it already carries its own
/// companion polygons. Metal traces and analysis-tool links never carry optical bands.
/// The same selection the Nazca export applies, so canvas and GDS agree. A connection
/// that is not routed yet is yielded with no segments (it still uses its layers).
/// </summary>
public static class WaveguideCompanionPathCollector
{
    /// <summary>Enumerates the band-carrying waveguides of <paramref name="canvas"/>.</summary>
    public static IEnumerable<WaveguideCompanionPath> Collect(DesignCanvasViewModel canvas)
    {
        var groups = WaveguideFilteringHelper.CollectAllGroups(canvas.Components.Select(c => c.Component));
        foreach (var connVm in canvas.Connections)
        {
            var conn = connVm.Connection;
            if (!CarriesBands(conn) || WaveguideFilteringHelper.IsConnectionInternalToAnyGroup(conn, groups))
                continue;
            yield return new WaveguideCompanionPath(conn.StartPin, conn.EndPin, conn.WidthMicrometers, conn.GetPathSegments());
        }

        foreach (var group in groups)
        {
            foreach (var frozen in group.InternalPaths)
            {
                if (ToPath(frozen) is { } path)
                    yield return path;
            }
        }

        foreach (var pathVm in canvas.CanvasFrozenPaths)
        {
            if (ToPath(pathVm.Path) is { } path)
                yield return path;
        }
    }

    private static bool CarriesBands(WaveguideConnection conn) =>
        conn.AsDrawnGeometry == null
        && conn.SourceGdsLayer == null
        && !conn.IsCrossChipletFacetLink
        && conn.StartPin?.ParentComponent?.IsAnalysisTool != true
        && conn.EndPin?.ParentComponent?.IsAnalysisTool != true
        && !IsMetal(conn.StartPin, conn.EndPin);

    private static WaveguideCompanionPath? ToPath(FrozenWaveguidePath frozen)
    {
        if (frozen.StartPin == null || frozen.EndPin == null || frozen.Layer != null || frozen.AsDrawnGeometry != null)
            return null;
        if (IsMetal(frozen.StartPin, frozen.EndPin) || frozen.Path?.Segments is not { Count: > 0 } segments)
            return null;
        return new WaveguideCompanionPath(frozen.StartPin, frozen.EndPin, frozen.WidthMicrometers, segments);
    }

    private static bool IsMetal(PhysicalPin? first, PhysicalPin? second) =>
        PinKindHelper.IsElectrical(first) && PinKindHelper.IsElectrical(second);
}
