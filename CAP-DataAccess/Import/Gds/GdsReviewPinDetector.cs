namespace CAP_DataAccess.Import.Gds;

/// <summary>Port analysis from a cell's source elements, independent of rendering budgets.</summary>
public static class GdsReviewPinDetector
{
    public sealed record Result(IReadOnlyList<DetectedPin> Pins, bool Limited);

    public static Result Detect(GdsCell cell, GdsBoundingBox bounds, GdsPinDetectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var detection = new FlattenedGdsCell { CellName = cell.Name };
        detection.Texts.AddRange(cell.Elements.OfType<GdsText>().Where(GdsReviewPortEvidence.IsLabel));
        var selected = options.WaveguideLayers.Concat(options.ElectricalLayers)
            .Concat(options.PortMarkerLayers ?? options.PortLayers).ToHashSet();
        var ownGeometry = cell.Elements.Where(e => e switch
        {
            GdsPolygon p => selected.Contains((p.Layer, p.DataType)),
            GdsPath p => selected.Contains((p.Layer, p.DataType)),
            _ => false,
        });
        var vertices = 0L;
        var limited = false;
        foreach (var element in ownGeometry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prospectiveVertices = element switch
            {
                GdsPolygon p => p.Points.Count,
                GdsPath p => (long)Math.Max(1, p.Points.Count - 1) * 5,
                _ => 0,
            };
            var prospectivePolygons = element is GdsPath path ? Math.Max(1, path.Points.Count - 1) : 1;
            if (vertices + prospectiveVertices > 2_000_000 || detection.Polygons.Count + prospectivePolygons > 100_000)
            { detection.Polygons.Clear(); limited = true; break; }
            foreach (var polygon in GdsPathOutliner.ExpandDrawnGeometry([element]))
            {
                cancellationToken.ThrowIfCancellationRequested();
                vertices += polygon.Points.Count;
                if (vertices > 2_000_000 || detection.Polygons.Count >= 100_000)
                {
                    // Incomplete geometry must never create edge/shape-derived ports.
                    detection.Polygons.Clear(); limited = true; break;
                }
                detection.Polygons.Add(polygon);
            }
            if (limited) break;
        }
        var arrows = detection.Polygons
            .Where(p => (options.PortMarkerLayers ?? options.PortLayers).Contains((p.Layer, p.DataType)))
            .Select(p => (Found: GdsReviewPortEvidence.TryArrow(p, out var tip, out var angle), Tip: tip, Angle: angle))
            .Where(a => a.Found).ToArray();
        // Recognized arrows locate and orient named ports. Unnamed individual arrows
        // also mark internal connections, so they must not create extra component pins.
        var arrowPolygons = detection.Polygons.Where(p => (options.PortMarkerLayers ?? options.PortLayers)
            .Contains((p.Layer, p.DataType)) && GdsReviewPortEvidence.TryArrow(p, out _, out _)).ToHashSet();
        detection.Polygons.RemoveAll(arrowPolygons.Contains);
        // Probe material and width at the true tip, not the offset text anchor.
        var directions = new Dictionary<GdsPoint, double>();
        for (var i = 0; i < detection.Texts.Count; i++)
        {
            var text = detection.Texts[i];
            if (!options.PortLayers.Contains((text.Layer, text.TextType))) continue;
            var nearby = arrows.Where(a => GdsReviewPortEvidence.DistanceSquared(a.Tip, text.Position) <= 4)
                .OrderBy(a => GdsReviewPortEvidence.DistanceSquared(a.Tip, text.Position)).ToArray();
            if (nearby.Length == 0) continue;
            var arrow = nearby[0];
            detection.Texts[i] = text with { Position = arrow.Tip };
            directions[arrow.Tip] = arrow.Angle;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var hasNamedPorts = detection.Texts.Any(t => options.PortLayers.Contains((t.Layer, t.TextType)));
        var pins = GdsPinDetector.Detect(detection, bounds, options)
            .Where(pin => !hasNamedPorts || pin.Source == DetectedPinSource.Label)
            .Select(pin =>
        {
            var anchor = new GdsPoint(pin.XUm + bounds.MinX, bounds.MaxY - pin.YUm);
            return pin.Source == DetectedPinSource.Label && directions.TryGetValue(anchor, out var angle)
                ? pin with { AngleDegrees = angle } : pin;
        }).ToArray();
        return new(GdsPinNameNormalizer.Normalize(pins, cell.Name, []), limited);
    }
}
