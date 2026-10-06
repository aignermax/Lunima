using CAP_Core.Components.Connections;

namespace CAP_Core.Analysis.LogicAnalysis;

/// <summary>
/// Computes the coupling loss one logic wire's hopped connection path picks up by
/// crossing chiplet edge-coupler links (issue #1445): every segment's
/// <see cref="ChipletEdgeCouplerCoupling.FieldFactor"/> — the same coupling model
/// the simulation charges, never a second one — multiplied in as a power factor.
/// Segments that are no cross-chiplet link return a field factor of exactly 1, so
/// they fall out of the product; a perfectly aligned link does too, which is why
/// an aligned crossing yields no entry and never warns.
/// </summary>
public static class WireLinkLossCalculator
{
    /// <summary>
    /// The link loss of one wire path, or null when no segment crosses a lossy
    /// link. The display name comes from the first lossy segment (a logic wire
    /// crossing several lossy links is pathological; the product still charges
    /// every one).
    /// </summary>
    public static LogicWireLinkLoss? ForPath(
        IReadOnlyList<WaveguideConnection> path, double wavelengthNm)
    {
        ArgumentNullException.ThrowIfNull(path);

        double coupling = 1.0;
        string? linkName = null;
        foreach (var segment in path)
        {
            double field = ChipletEdgeCouplerCoupling.FieldFactor(segment, wavelengthNm);
            if (field >= 1.0)
                continue;
            coupling *= field * field;
            linkName ??= Describe(segment);
        }
        return linkName == null ? null : new LogicWireLinkLoss(coupling, linkName);
    }

    /// <summary>
    /// Names one link the way the Design Checks findings do: the two chiplet group
    /// names as <c>'Chiplet A' / 'Chiplet B'</c>. Only called for segments that
    /// qualified as cross-chiplet links, so both endpoints resolve to facets.
    /// </summary>
    private static string Describe(WaveguideConnection link)
    {
        ChipletInterfaceChecker.TryGetFacet(link.StartPin, out var start);
        ChipletInterfaceChecker.TryGetFacet(link.EndPin, out var end);
        return $"'{start.Chiplet?.GroupName}' / '{end.Chiplet?.GroupName}'";
    }
}
