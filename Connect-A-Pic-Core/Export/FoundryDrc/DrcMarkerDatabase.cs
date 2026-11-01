using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CAP_Core.Export.FoundryDrc;

/// <summary>One flagged geometry of a foundry DRC run, in the GDS's own frame (µm, y up).</summary>
/// <param name="Rule">Rule (category) name.</param>
/// <param name="Cell">Cell the marker belongs to.</param>
/// <param name="MinX">Bounding box of the marker.</param>
/// <param name="MinY">Bounding box of the marker.</param>
/// <param name="MaxX">Bounding box of the marker.</param>
/// <param name="MaxY">Bounding box of the marker.</param>
public sealed record DrcMarker(string Rule, string Cell, double MinX, double MinY, double MaxX, double MaxY);

/// <summary>A rule of the deck: its description and how often it was violated.</summary>
public sealed record DrcRuleSummary(string Rule, string Description, int Violations);

/// <summary>
/// Reads a KLayout marker database (<c>.lyrdb</c>): every rule the deck checked — including the
/// clean ones — and every violation marker with its bounding box. Marker values are KLayout
/// shape strings (<c>box: (x1,y1;x2,y2)</c>, <c>polygon: (…)</c>, <c>edge-pair: (…)/(…)</c>) in µm.
/// </summary>
public static class DrcMarkerDatabase
{
    private static readonly Regex Number = new(@"-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

    /// <summary>Parses <paramref name="xml"/>; throws <see cref="FormatException"/> for a corrupt report.</summary>
    public static (IReadOnlyList<DrcRuleSummary> Rules, IReadOnlyList<DrcMarker> Markers) Parse(string xml)
    {
        XDocument document;
        try { document = XDocument.Parse(xml); }
        catch (System.Xml.XmlException ex) { throw new FormatException($"Unreadable DRC report: {ex.Message}", ex); }

        var root = document.Root ?? throw new FormatException("Empty DRC report.");
        var markers = root.Element("items")?.Elements("item")
            .Select(ReadMarker)
            .Where(m => m != null)
            .Select(m => m!)
            .ToList() ?? new List<DrcMarker>();
        var counts = markers.GroupBy(m => m.Rule).ToDictionary(g => g.Key, g => g.Count());
        var rules = root.Element("categories")?.Descendants("category")
            .Select(c => Unquote(c.Element("name")?.Value))
            .Where(name => name.Length > 0)
            .Distinct()
            .Select(name => new DrcRuleSummary(name, Describe(root, name), counts.GetValueOrDefault(name)))
            .ToList() ?? new List<DrcRuleSummary>();
        foreach (var missing in counts.Keys.Except(rules.Select(r => r.Rule)))
            rules.Add(new DrcRuleSummary(missing, "", counts[missing]));
        return (rules, markers);
    }

    private static DrcMarker? ReadMarker(XElement item)
    {
        var rule = Unquote(item.Element("category")?.Value);
        var cell = Unquote(item.Element("cell")?.Value);
        var coordinates = item.Element("values")?.Elements("value")
            .SelectMany(v => Number.Matches(v.Value).Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)))
            .ToList() ?? new List<double>();
        if (coordinates.Count < 2)
            return new DrcMarker(rule, cell, 0, 0, 0, 0);
        var xs = coordinates.Where((_, i) => i % 2 == 0).ToList();
        var ys = coordinates.Where((_, i) => i % 2 == 1).ToList();
        return new DrcMarker(rule, cell, xs.Min(), ys.Min(), xs.Max(), ys.Max());
    }

    private static string Describe(XElement root, string rule) =>
        root.Element("categories")?.Descendants("category")
            .FirstOrDefault(c => Unquote(c.Element("name")?.Value) == rule)?.Element("description")?.Value ?? "";

    // Category paths are written quoted ('rule' or 'group'.'rule'); keep the last segment.
    private static string Unquote(string? value) =>
        (value ?? "").Split('.').Last().Trim().Trim('\'');
}
