namespace CAP_DataAccess.Import.Gds;

/// <summary>Content evidence for a suggested port layer; no material role is inferred.</summary>
public sealed record GdsReviewPortSuggestion(GdsLayerAssignment Assignment, int MatchedLabelCount);

/// <summary>Recognizes dedicated arrow layers corroborated by nearby labels in the same source cell.</summary>
public static class GdsReviewPortEvidence
{
    public static IReadOnlyList<GdsReviewPortSuggestion> Suggest(GdsLibrary library, string topCell,
        CancellationToken cancellationToken = default)
    {
        var reachable = new HashSet<string>();
        var pending = new Stack<string>(); pending.Push(topCell);
        var cells = new List<GdsCell>();
        while (pending.TryPop(out var name))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!reachable.Add(name) || !library.Cells.TryGetValue(name, out var cell)) continue;
            cells.Add(cell);
            foreach (var reference in cell.Elements.OfType<GdsReference>()) pending.Push(reference.CellName);
        }
        // Reject a candidate layer if it also carries real device geometry anywhere reachable.
        var markerLayers = cells.SelectMany(c => c.Elements.OfType<GdsPolygon>())
            .GroupBy(p => (p.Layer, p.DataType))
            .Where(g => g.All(p => TryArrow(p, out _, out _)))
            .Select(g => g.Key).ToHashSet();
        markerLayers.ExceptWith(cells.SelectMany(c => c.Elements.OfType<GdsPath>()).Select(p => (p.Layer, p.DataType)));
        var labelTotals = cells.SelectMany(c => c.Elements.OfType<GdsText>()).Where(IsLabel)
            .GroupBy(t => (t.Layer, t.TextType)).ToDictionary(g => g.Key, g => g.Count());
        var counts = new Dictionary<(int Layer, int Datatype, GdsLayerRole Role), int>();
        foreach (var cell in cells)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var arrows = cell.Elements.OfType<GdsPolygon>()
                .Where(p => markerLayers.Contains((p.Layer, p.DataType)))
                .Select(p => { TryArrow(p, out var tip, out _); return (Polygon: p, Tip: tip); }).ToArray();
            foreach (var text in cell.Elements.OfType<GdsText>().Where(IsLabel))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var nearby = arrows.Where(a => DistanceSquared(a.Tip, text.Position) <= 4).ToArray();
                if (nearby.Length == 0) continue;
                Add((text.Layer, text.TextType, GdsLayerRole.PortLabel));
                foreach (var pair in nearby.Select(a => (a.Polygon.Layer, a.Polygon.DataType)).Distinct())
                    Add((pair.Layer, pair.DataType, GdsLayerRole.PortMarker));
            }
        }
        var arrowSuggestions = counts.Where(x => x.Key.Role != GdsLayerRole.PortLabel
                || x.Value == labelTotals[(x.Key.Layer, x.Key.Datatype)])
            .GroupBy(x => (x.Key.Layer, x.Key.Datatype))
            .OrderBy(g => g.Key.Layer).ThenBy(g => g.Key.Datatype)
            .Select(g => new GdsReviewPortSuggestion(new(g.Key.Layer, g.Key.Datatype,
                g.Aggregate(GdsLayerRole.Unknown, (role, x) => role | x.Key.Role)), g.Max(x => x.Value))).ToArray();
        return arrowSuggestions.Concat(GdsReviewEndFaceEvidence.Suggest(cells, cancellationToken))
            .GroupBy(s => (s.Assignment.Layer, s.Assignment.Datatype))
            .OrderBy(g => g.Key.Layer).ThenBy(g => g.Key.Datatype)
            .Select(g => new GdsReviewPortSuggestion(new(g.Key.Layer, g.Key.Datatype,
                g.Aggregate(GdsLayerRole.Unknown, (role, s) => role | s.Assignment.Role)),
                g.Max(s => s.MatchedLabelCount))).ToArray();
        void Add((int, int, GdsLayerRole) key) => counts[key] = counts.GetValueOrDefault(key) + 1;
    }

    internal static bool IsLabel(GdsText text) => !string.IsNullOrWhiteSpace(text.Text)
        && !text.Text.Contains('\n') && !text.Text.Contains('\r') && !GdsGhostLabelFilter.IsGhost(text);

    internal static double DistanceSquared(GdsPoint a, GdsPoint b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    /// <summary>Seven-corner, symmetric arrow with a recessed shaft, in any rotation/reflection.</summary>
    internal static bool TryArrow(GdsPolygon polygon, out GdsPoint tip, out double angle)
    {
        tip = default; angle = 0;
        var p = polygon.Points;
        var n = p.Count > 1 && p[0] == p[^1] ? p.Count - 1 : p.Count;
        if (n != 7) return false;
        const double tolerance = .003; // Allow nanometer rounding of rotated markers.
        for (var start = 0; start < n; start++)
        {
            var origin = p[start];
            var tailA = p[(start + 3) % n]; var tailB = p[(start + 4) % n];
            var dx = (tailA.X + tailB.X) / 2 - origin.X;
            var dy = (tailA.Y + tailB.Y) / 2 - origin.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= tolerance || length > 6) continue;
            dx /= length; dy /= length;
            var u = new double[7]; var v = new double[7];
            for (var i = 1; i < 7; i++)
            {
                var point = p[(start + i) % n];
                var x = point.X - origin.X; var y = point.Y - origin.Y;
                u[i] = x * dx + y * dy; v[i] = -x * dy + y * dx;
            }
            bool symmetric = true;
            for (var i = 1; i <= 3; i++)
                symmetric &= Math.Abs(u[i] - u[7 - i]) < tolerance && Math.Abs(v[i] + v[7 - i]) < tolerance;
            if (!symmetric || u[1] <= tolerance || Math.Abs(u[1] - u[2]) > tolerance
                || u[3] <= u[2] + tolerance || Math.Abs(v[2] - v[3]) > tolerance
                || v[1] * v[2] <= 0 || Math.Abs(v[1]) <= Math.Abs(v[2]) + tolerance
                || Math.Abs(v[2]) <= tolerance || Math.Abs(v[1]) > 3) continue;
            tip = origin;
            angle = (Math.Atan2(dy, -dx) * 180 / Math.PI + 360) % 360;
            return true;
        }
        return false;
    }
}
