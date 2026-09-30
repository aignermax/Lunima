using System.Globalization;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Analysis;

/// <summary>
/// DRC-lite rule for multi-chip assemblies (issue #1219, rung 6): checks every connection
/// whose two endpoints are edge-coupler facet pins on DIFFERENT chiplets (top-level
/// <see cref="ComponentGroup"/>s) and warns when the butt-coupled pair is misaligned —
/// the failure mode #1214 proved simulates cleanly but that kills the physical link
/// (butt-coupling loss grows fast with lateral offset; ~2 µm on a ~3 µm mode is already
/// several dB). Three checks per link:
/// 1. Facing — the facet pin directions must be antiparallel within
///    <see cref="FacingToleranceDegrees"/>.
/// 2. Lateral alignment — the offset perpendicular to the pin axis must not exceed
///    <see cref="MaxLateralOffsetMicrometers"/> (only measured when the pair faces each
///    other — a perpendicular offset is meaningless between crossed axes).
/// 3. At the edge — each facet pin must sit on its chiplet's outer boundary (the group's
///    bounding box) within <see cref="EdgeToleranceMicrometers"/>, measured along the
///    direction the pin faces.
/// All findings are warnings (DRC-lite, not foundry DRC). The S-matrix model is
/// deliberately unchanged — a gap/offset-dependent coupling loss is a possible follow-up.
/// </summary>
/// <remarks>
/// Edge-coupler heuristic: a component counts as an edge coupler when its
/// <see cref="Component.TemplateName"/> stamp (applied at placement and on load, #1214)
/// contains "edge coupler" (case-insensitive) — the naming every bundled PDK follows
/// (demo PDK "Edge Coupler"). Components without a template stamp (built-ins, hand-made
/// groups) are never flagged. Chiplets are identified by walking
/// <see cref="Component.ParentGroup"/> to the top-level group; a link between two
/// ungrouped edge couplers, or inside one chiplet, is not this rule's concern.
/// </remarks>
public class ChipletInterfaceChecker
{
    /// <summary>Angular tolerance below which two facet pins count as facing each other.</summary>
    public const double FacingToleranceDegrees = 1.0;

    /// <summary>Maximum allowed offset perpendicular to the pin axis, in micrometers.</summary>
    public const double MaxLateralOffsetMicrometers = 0.5;

    /// <summary>Tolerance within which a facet pin counts as lying on the chiplet boundary.</summary>
    public const double EdgeToleranceMicrometers = 1.0;

    private const string EdgeCouplerTemplateToken = "edge coupler";
    private const double DirectionEpsilon = 1e-9;

    /// <summary>
    /// Checks all provided connections for cross-chiplet edge-coupler misalignment.
    /// </summary>
    /// <param name="connections">The connections to check; non-edge-coupler and
    /// same-chiplet links are skipped.</param>
    /// <returns>One issue per finding, empty when every cross-chiplet link is aligned.</returns>
    public List<DesignIssue> Check(IEnumerable<WaveguideConnection> connections)
    {
        ArgumentNullException.ThrowIfNull(connections);

        var issues = new List<DesignIssue>();
        foreach (var connection in connections)
        {
            if (!TryGetFacet(connection.StartPin, out var start)
                || !TryGetFacet(connection.EndPin, out var end)
                || ReferenceEquals(start.Chiplet, end.Chiplet))
            {
                continue;
            }

            CheckLink(connection, start, end, issues);
        }
        return issues;
    }

    private static void CheckLink(
        WaveguideConnection connection, FacetPin start, FacetPin end, List<DesignIssue> issues)
    {
        var (startX, startY) = start.Pin.GetAbsolutePosition();
        var (endX, endY) = end.Pin.GetAbsolutePosition();
        double midX = (startX + endX) / 2;
        double midY = (startY + endY) / 2;
        string nameA = start.Chiplet.GroupName;
        string nameB = end.Chiplet.GroupName;

        double deviation = AntiparallelDeviation(start.Pin.GetAbsoluteAngle(), end.Pin.GetAbsoluteAngle());
        if (deviation > FacingToleranceDegrees)
        {
            issues.Add(new DesignIssue(
                DesignIssueType.ChipletInterfaceNotFacing,
                connection,
                midX,
                midY,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Edge couplers on '{nameA}' / '{nameB}' do not face each other ({deviation:F1}° off axis)")));
        }
        else
        {
            double lateral = LateralOffset(startX, startY, endX, endY, start.Pin.GetAbsoluteAngle());
            if (lateral > MaxLateralOffsetMicrometers)
            {
                issues.Add(new DesignIssue(
                    DesignIssueType.ChipletInterfaceLateralOffset,
                    connection,
                    midX,
                    midY,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Edge couplers on '{nameA}' / '{nameB}' are laterally offset by {lateral:F2} µm"
                        + $" (max {MaxLateralOffsetMicrometers} µm)")));
            }
        }

        CheckAtEdge(connection, start, issues);
        CheckAtEdge(connection, end, issues);
    }

    /// <summary>Flags the facet pin when it does not lie on its chiplet's bounding-box edge.</summary>
    private static void CheckAtEdge(
        WaveguideConnection connection, FacetPin facet, List<DesignIssue> issues)
    {
        var (pinX, pinY) = facet.Pin.GetAbsolutePosition();
        double distance = DistanceToEdgeAlongFacing(
            pinX, pinY, facet.Pin.GetAbsoluteAngle(), BoundsOf(facet.Chiplet));
        if (distance <= EdgeToleranceMicrometers)
            return;

        issues.Add(new DesignIssue(
            DesignIssueType.ChipletInterfaceOffEdge,
            connection,
            pinX,
            pinY,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Edge coupler '{facet.Pin.ParentComponent.Identifier}.{facet.Pin.Name}' on"
                + $" '{facet.Chiplet.GroupName}' sits {distance:F1} µm off the chiplet edge it faces")));
    }

    /// <summary>
    /// Resolves a connection endpoint to an edge-coupler facet pin with its top-level
    /// chiplet group; false when the pin is not an edge-coupler pin on a grouped chiplet.
    /// </summary>
    private static bool TryGetFacet(PhysicalPin? pin, out FacetPin facet)
    {
        facet = default;
        var component = pin?.ParentComponent;
        if (component == null || !IsEdgeCoupler(component))
            return false;

        var chiplet = TopLevelGroupOf(component);
        if (chiplet == null)
            return false;

        facet = new FacetPin(pin!, chiplet);
        return true;
    }

    private static bool IsEdgeCoupler(Component component) =>
        component.TemplateName?.Contains(EdgeCouplerTemplateToken, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Walks the parent-group chain to the outermost group (the chiplet).</summary>
    private static ComponentGroup? TopLevelGroupOf(Component component)
    {
        var group = component.ParentGroup as ComponentGroup;
        if (group == null)
            return null;
        while (group.ParentGroup != null)
        {
            group = group.ParentGroup;
        }
        return group;
    }

    /// <summary>Angular distance from the antiparallel ideal, in degrees (0 = perfectly facing).</summary>
    private static double AntiparallelDeviation(double angleA, double angleB)
    {
        double diff = Math.Abs(angleA - angleB) % 360.0;
        if (diff > 180.0)
            diff = 360.0 - diff;
        return 180.0 - diff;
    }

    /// <summary>Magnitude of the end-pin offset perpendicular to the start pin's axis.</summary>
    private static double LateralOffset(
        double startX, double startY, double endX, double endY, double startAngleDegrees)
    {
        double radians = startAngleDegrees * Math.PI / 180.0;
        double dirX = Math.Cos(radians);
        double dirY = Math.Sin(radians);
        return Math.Abs((endX - startX) * dirY - (endY - startY) * dirX);
    }

    /// <summary>The chiplet's outer boundary: the group's stored bounding box, the same
    /// rectangle the canvas renders as the group outline.</summary>
    private static (double MinX, double MinY, double MaxX, double MaxY) BoundsOf(ComponentGroup chiplet)
    {
        double minX = chiplet.PhysicalX + chiplet.MinChildOffsetX;
        double minY = chiplet.PhysicalY + chiplet.MinChildOffsetY;
        return (minX, minY, minX + chiplet.WidthMicrometers, minY + chiplet.HeightMicrometers);
    }

    /// <summary>
    /// Distance from the pin to the chiplet boundary along the direction the pin faces
    /// (the edge the facet should sit on). A pin outside the boundary is measured by its
    /// straight-line distance to the rectangle instead.
    /// </summary>
    private static double DistanceToEdgeAlongFacing(
        double pinX, double pinY, double angleDegrees,
        (double MinX, double MinY, double MaxX, double MaxY) bounds)
    {
        if (pinX < bounds.MinX || pinX > bounds.MaxX || pinY < bounds.MinY || pinY > bounds.MaxY)
        {
            double dx = Math.Max(bounds.MinX - pinX, Math.Max(0, pinX - bounds.MaxX));
            double dy = Math.Max(bounds.MinY - pinY, Math.Max(0, pinY - bounds.MaxY));
            return Math.Sqrt(dx * dx + dy * dy);
        }

        double radians = angleDegrees * Math.PI / 180.0;
        double dirX = Math.Cos(radians);
        double dirY = Math.Sin(radians);
        double tX = ExitDistance(pinX, dirX, bounds.MinX, bounds.MaxX);
        double tY = ExitDistance(pinY, dirY, bounds.MinY, bounds.MaxY);
        return Math.Min(tX, tY);
    }

    /// <summary>Ray parameter at which an axis-aligned ray exits a [min, max] interval.</summary>
    private static double ExitDistance(double position, double direction, double min, double max)
    {
        if (direction > DirectionEpsilon)
            return (max - position) / direction;
        if (direction < -DirectionEpsilon)
            return (min - position) / direction;
        return double.PositiveInfinity;
    }

    private readonly record struct FacetPin(PhysicalPin Pin, ComponentGroup Chiplet);
}
