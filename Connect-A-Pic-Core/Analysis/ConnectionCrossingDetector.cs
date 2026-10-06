using CAP_Core.Components.Connections;
using CAP_Core.Routing;

namespace CAP_Core.Analysis;

/// <summary>
/// Detects connection×connection waveguide crossings: two routed connections whose
/// paths properly cross (per <see cref="PathIntersectionDetector.Crosses"/>) without a
/// crossing component at the intersection. Such a crossing exports as overlapping
/// geometry and fails foundry verification, so every pair is reported as an error-level
/// <see cref="DesignIssue"/> naming both connections. Pairs that only touch at a shared
/// pin or endpoint are not crossings and are skipped.
/// </summary>
public class ConnectionCrossingDetector
{
    /// <summary>String-table key for the localized crossing message ("{0}" and "{1}" are the two connection labels).</summary>
    public const string CrossingLocalizationKey = "DesignChecks.WaveguideCrossing";

    /// <summary>
    /// Checks every pair of routed connections for a proper path crossing and returns
    /// one issue per crossing pair. Connections without a routed path are ignored, as
    /// are blocked fallbacks: their path is a placeholder straight line that is already
    /// reported as <see cref="DesignIssueType.BlockedPath"/>, so its incidental
    /// crossings carry no actionable information.
    /// </summary>
    /// <param name="connections">The waveguide connections to check pairwise.</param>
    /// <returns>A list of crossing issues, empty when no two connections cross.</returns>
    public List<DesignIssue> DetectCrossings(IEnumerable<WaveguideConnection> connections)
    {
        ArgumentNullException.ThrowIfNull(connections);

        var routed = connections
            .Where(c => c.RoutedPath?.Segments is { Count: > 0 } && !c.IsBlockedFallback)
            .ToList();

        // Sampled once per path, not once per pair.
        var sampled = routed.Select(c => SampledPolyline.From(c.RoutedPath!)).ToList();
        var issues = new List<DesignIssue>();
        for (int i = 0; i < routed.Count; i++)
        {
            for (int j = i + 1; j < routed.Count; j++)
            {
                if (SharesEndpointPin(routed[i], routed[j]))
                    continue;
                if (!sampled[i].Crosses(sampled[j]))
                    continue;
                issues.Add(CreateCrossingIssue(routed[i], routed[j]));
            }
        }
        return issues;
    }

    /// <summary>
    /// True when the two connections share any endpoint pin — such pairs only ever
    /// touch at that pin and never count as crossings.
    /// </summary>
    private static bool SharesEndpointPin(WaveguideConnection a, WaveguideConnection b)
    {
        return ReferenceEquals(a.StartPin, b.StartPin)
            || ReferenceEquals(a.StartPin, b.EndPin)
            || ReferenceEquals(a.EndPin, b.StartPin)
            || ReferenceEquals(a.EndPin, b.EndPin);
    }

    /// <summary>
    /// Builds the error-level issue for a crossing pair. The location is the midpoint
    /// of the first connection's endpoints; both connections are named in the message.
    /// </summary>
    private static DesignIssue CreateCrossingIssue(WaveguideConnection a, WaveguideConnection b)
    {
        var labelA = FormatConnectionLabel(a);
        var labelB = FormatConnectionLabel(b);
        var (startX, startY) = a.StartPin.GetAbsolutePosition();
        var (endX, endY) = a.EndPin.GetAbsolutePosition();

        return new DesignIssue(
            DesignIssueType.WaveguideCrossing,
            a,
            (startX + endX) / 2,
            (startY + endY) / 2,
            $"Waveguide crossing: {labelA} crosses {labelB} — no crossing component at the intersection; the exported layout will fail overlap checks",
            CrossingLocalizationKey,
            new object[] { labelA, labelB });
    }

    /// <summary>
    /// Formats a connection label as "ComponentA.Pin → ComponentB.Pin".
    /// </summary>
    private static string FormatConnectionLabel(WaveguideConnection conn)
    {
        var start = $"{conn.StartPin.ParentComponent.Identifier}.{conn.StartPin.Name}";
        var end = $"{conn.EndPin.ParentComponent.Identifier}.{conn.EndPin.Name}";
        return $"{start} → {end}";
    }
}
