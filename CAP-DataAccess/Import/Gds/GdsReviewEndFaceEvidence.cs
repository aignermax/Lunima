using System.Text.RegularExpressions;

namespace CAP_DataAccess.Import.Gds;

/// <summary>Independent label-only evidence from narrow polygon end faces, without material/layer conventions.</summary>
internal static class GdsReviewEndFaceEvidence
{
    private const double PositionTolerance = .002;
    private static readonly Regex PortName = new(
        @"^(?:o|e|a|b|in|out|pin|port|g|s|n|p)[0-9]{1,4}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<GdsReviewPortSuggestion> Suggest(IReadOnlyList<GdsCell> cells,
        CancellationToken cancellationToken)
    {
        var labelPairs = cells.SelectMany(c => c.Elements.OfType<GdsText>()).Where(GdsReviewPortEvidence.IsLabel)
            .GroupBy(t => (t.Layer, t.TextType))
            .Where(g => g.All(t => PortName.IsMatch(t.Text.Trim())))
            .Select(g => g.Key).ToHashSet();
        foreach (var element in cells.SelectMany(c => c.Elements))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Mixed shape/text layers require stronger arrow evidence or user assignment.
            if (element is GdsPolygon polygon) labelPairs.Remove((polygon.Layer, polygon.DataType));
            else if (element is GdsPath path) labelPairs.Remove((path.Layer, path.DataType));
        }
        var evidence = cells.ToDictionary(c => c.Name, c => new CellEvidence(c,
            c.Elements.OfType<GdsText>().Where(t => labelPairs.Contains((t.Layer, t.TextType))
                && GdsReviewPortEvidence.IsLabel(t)).ToArray()));
        foreach (var item in evidence.Values) MatchOwnGeometry(item, cancellationToken);
        var resolved = new HashSet<string>();
        foreach (var cell in cells) Resolve(cell.Name, new HashSet<string>());

        // A layer assignment enables every label on that pair. Therefore every
        // eligible label must be proved, including parent labels above references.
        return evidence.Values.SelectMany(e => e.Labels.Select(t => (Owner: e, Text: t)))
            .GroupBy(x => (x.Text.Layer, x.Text.TextType))
            .Where(g => g.All(x => x.Owner.Matched.Contains(x.Text))
                && g.Select(x => (x.Owner.Cell.Name, x.Text.Text.Trim())).Distinct().Count() >= 2
                && g.Select(x => (x.Owner.Cell.Name, x.Text.Position)).Distinct().Count() >= 2)
            .Select(g => new GdsReviewPortSuggestion(new(g.Key.Layer, g.Key.TextType, GdsLayerRole.PortLabel),
                g.Select(x => (x.Owner.Cell.Name, x.Text.Text.Trim())).Distinct().Count())).ToArray();

        IReadOnlyCollection<GdsText> Resolve(string name, HashSet<string> active)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!evidence.TryGetValue(name, out var parent) || active.Count >= 64 || !active.Add(name)) return [];
            if (resolved.Contains(name)) { active.Remove(name); return parent.Matched; }
            var inspected = 0;
            foreach (var reference in parent.Cell.Elements.OfType<GdsReference>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                // No array expansion, absolute transforms, or unbounded anchor propagation.
                if (reference.Rows != 1 || reference.Columns != 1 || (reference.TransformFlags & 6) != 0) continue;
                var anchors = Resolve(reference.CellName, active);
                var transform = GdsTransform.FromReference(reference, 0, 0);
                foreach (var anchor in anchors)
                {
                    if (++inspected > 100_000) break;
                    MatchPoint(parent, transform.Apply(anchor.Position));
                }
                if (inspected > 100_000) break;
            }
            active.Remove(name); resolved.Add(name);
            return parent.Matched;
        }
    }

    private static void MatchOwnGeometry(CellEvidence evidence, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (evidence.Labels.Length == 0) return;
        var polygons = evidence.Cell.Elements.OfType<GdsPolygon>().ToArray();
        if (polygons.Sum(p => (long)p.Points.Count) > 2_000_000) return;
        // Only labels are spatially indexed; each source edge is visited once.
        foreach (var polygon in polygons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var points = polygon.Points;
            if (points.Count < 3) continue;
            for (int i = 0, previous = points.Count - 1; i < points.Count; previous = i++)
            {
                if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                var a = points[previous]; var b = points[i];
                var widthSquared = GdsReviewPortEvidence.DistanceSquared(a, b);
                if (widthSquared < .01 || widthSquared > 100) continue;
                MatchPoint(evidence, new((a.X + b.X) / 2, (a.Y + b.Y) / 2));
            }
        }
    }

    private static void MatchPoint(CellEvidence evidence, GdsPoint point)
    {
        var (x, y) = Bucket(point);
        for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                if (!evidence.Buckets.TryGetValue((x + dx, y + dy), out var nearby)) continue;
                foreach (var label in nearby)
                    if (GdsReviewPortEvidence.DistanceSquared(label.Position, point) <= PositionTolerance * PositionTolerance)
                        evidence.Matched.Add(label);
            }
    }

    private sealed class CellEvidence(GdsCell cell, GdsText[] labels)
    {
        public GdsCell Cell { get; } = cell;
        public GdsText[] Labels { get; } = labels;
        public HashSet<GdsText> Matched { get; } = [];
        public Dictionary<(long X, long Y), GdsText[]> Buckets { get; } =
            labels.GroupBy(t => Bucket(t.Position)).ToDictionary(g => g.Key, g => g.ToArray());
    }

    private static (long X, long Y) Bucket(GdsPoint point) =>
        ((long)Math.Floor(point.X / PositionTolerance), (long)Math.Floor(point.Y / PositionTolerance));
}
