using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Grid;

namespace CAP_Core.Analysis;

/// <summary>
/// Computes the one-click "Align chiplet" fix (issue #1248, rung 6): the translation of
/// the end pin's chiplet that brings a misaligned cross-chiplet edge-coupler link
/// (<see cref="ChipletInterfaceChecker"/>) to zero lateral offset and zero axial gap.
/// "Aligned" means exactly what the checker means — the same facet detection, the same
/// geometry helpers — so a link the aligner fixes can never still warn.
/// The planner only computes; applying the returned plan (as an undoable group move) is
/// the caller's job. Refusals carry a <see cref="ChipletAlignmentRefusal"/> reason the
/// UI can localize: non-facing facets (rotation is out of scope), a blocking component
/// or chiplet at the target position (the same <see cref="GroupCollisionDetector"/> the
/// drag-drop placement uses), or a third-chiplet link the move would break.
/// </summary>
public class ChipletLinkAligner
{
    private const double ZeroToleranceMicrometers = 1e-9;

    /// <summary>
    /// Matches <c>GroupCollisionDetector</c>'s comfort gap: pairs that touch within this
    /// distance count as abutting (allowed for butt-coupled facets), pairs that strictly
    /// interpenetrate count as blocking. Kept private in the detector, so mirrored here.
    /// </summary>
    private const double PlacementGapMicrometers = 5.0;

    /// <summary>Depth below which two rectangles count as touching, not interpenetrating.</summary>
    private const double InterpenetrationEpsilonMicrometers = 1e-6;

    /// <summary>
    /// Plans the alignment of <paramref name="connection"/>'s end chiplet.
    /// </summary>
    /// <param name="connection">The cross-chiplet edge-coupler link to align.</param>
    /// <param name="allConnections">Every connection on the canvas — consulted for
    /// third-chiplet links the move would break.</param>
    /// <param name="allComponents">Every top-level component on the canvas — consulted
    /// for collisions at the target position.</param>
    /// <param name="wavelengthNm">Wavelength the gap check is evaluated at — pass the
    /// simulation wavelength so planner and checker agree.</param>
    /// <param name="plan">The translation to apply, when planning succeeds.</param>
    /// <param name="refusal">Why planning failed, when it did.</param>
    /// <returns>True when the link can be aligned by a pure translation.</returns>
    public bool TryPlan(
        WaveguideConnection connection,
        IEnumerable<WaveguideConnection> allConnections,
        IEnumerable<Component> allComponents,
        double wavelengthNm,
        out ChipletAlignmentPlan? plan,
        out ChipletAlignmentRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(allConnections);
        ArgumentNullException.ThrowIfNull(allComponents);

        plan = null;
        if (!ChipletInterfaceChecker.TryGetFacet(connection.StartPin, out var start)
            || !ChipletInterfaceChecker.TryGetFacet(connection.EndPin, out var end)
            || ReferenceEquals(start.Chiplet, end.Chiplet))
        {
            refusal = ChipletAlignmentRefusal.NotCrossChipletLink;
            return false;
        }

        double deviation = ChipletInterfaceChecker.AntiparallelDeviation(
            start.Pin.GetAbsoluteAngle(), end.Pin.GetAbsoluteAngle());
        if (deviation > ChipletInterfaceChecker.FacingToleranceDegrees)
        {
            refusal = ChipletAlignmentRefusal.NotFacing;
            return false;
        }

        var (startX, startY) = start.Pin.GetAbsolutePosition();
        var (endX, endY) = end.Pin.GetAbsolutePosition();
        // Coincident pins are exactly zero lateral offset and zero axial gap in the
        // checker's own decomposition (LateralOffset / AxialGap of the zero vector).
        double deltaX = startX - endX;
        double deltaY = startY - endY;
        if (Math.Abs(deltaX) < ZeroToleranceMicrometers && Math.Abs(deltaY) < ZeroToleranceMicrometers)
        {
            refusal = ChipletAlignmentRefusal.AlreadyAligned;
            return false;
        }

        var components = allComponents as ICollection<Component> ?? allComponents.ToList();
        if (IsBlockedAtTarget(end.Chiplet, deltaX, deltaY, components))
        {
            refusal = ChipletAlignmentRefusal.Overlap;
            return false;
        }

        if (WouldBreakOtherLink(connection, end.Chiplet, deltaX, deltaY, allConnections, wavelengthNm))
        {
            refusal = ChipletAlignmentRefusal.WouldBreakOtherLink;
            return false;
        }

        plan = new ChipletAlignmentPlan(end.Chiplet, deltaX, deltaY);
        refusal = default;
        return true;
    }

    /// <summary>
    /// Tentatively applies the translation and checks whether any OTHER cross-chiplet
    /// link of the moved chiplet that is currently aligned would start warning.
    /// The tentative move is always reverted.
    /// </summary>
    private static bool WouldBreakOtherLink(
        WaveguideConnection target,
        ComponentGroup movedChiplet,
        double deltaX,
        double deltaY,
        IEnumerable<WaveguideConnection> allConnections,
        double wavelengthNm)
    {
        var checker = new ChipletInterfaceChecker();
        var others = allConnections
            .Where(c => !ReferenceEquals(c, target))
            .Where(c => LinksChiplet(c, movedChiplet))
            .ToList();
        if (others.Count == 0)
            return false;

        var cleanBefore = others
            .Select(c => checker.Check(new[] { c }, wavelengthNm).Count == 0)
            .ToList();

        movedChiplet.MoveGroup(deltaX, deltaY);
        bool breaks;
        try
        {
            breaks = others
                .Select((c, i) => cleanBefore[i] && checker.Check(new[] { c }, wavelengthNm).Count > 0)
                .Any(flagged => flagged);
        }
        finally
        {
            movedChiplet.MoveGroup(-deltaX, -deltaY);
        }
        return breaks;
    }

    /// <summary>
    /// True when the moved chiplet cannot occupy its target position. Reuses the
    /// placement overlap check (<see cref="GroupCollisionDetector.CanPlaceGroup"/>) with
    /// one refinement: pairs that merely ABUT at the target (the butt-coupled facet
    /// pair this fix creates, and any other touching pair) are grandfathered like the
    /// drag-drop flow grandfathers pre-existing touches, while pairs that strictly
    /// interpenetrate refuse outright.
    /// </summary>
    private static bool IsBlockedAtTarget(
        ComponentGroup chiplet, double deltaX, double deltaY, ICollection<Component> allComponents)
    {
        var movedLeaves = chiplet.GetAllComponentsRecursive()
            .Where(c => c is not ComponentGroup)
            .ToList();
        var memberSet = new HashSet<Component>(chiplet.GetAllComponentsRecursive()) { chiplet };
        var partnerLeaves = LeavesOutside(allComponents, memberSet);

        var abuttingPairs = new Dictionary<Component, HashSet<Component>>();
        foreach (var leaf in movedLeaves)
        {
            foreach (var partner in partnerLeaves)
            {
                double overlapX = OverlapDepth(
                    leaf.PhysicalX + deltaX, leaf.WidthMicrometers, partner.PhysicalX, partner.WidthMicrometers);
                double overlapY = OverlapDepth(
                    leaf.PhysicalY + deltaY, leaf.HeightMicrometers, partner.PhysicalY, partner.HeightMicrometers);
                double penetration = Math.Min(overlapX, overlapY);
                if (penetration > InterpenetrationEpsilonMicrometers)
                    return true; // genuine interpenetration — a blocking component or chiplet
                bool touching = penetration > -InterpenetrationEpsilonMicrometers
                    && Math.Max(overlapX, overlapY) > 0;
                if (touching)
                {
                    // Face-to-face abutment along an interface line: allowed — the whole
                    // point of the fix is butt-coupling, which the detector's comfort
                    // gap (<see cref="PlacementGapMicrometers"/>) would otherwise refuse.
                    if (!abuttingPairs.TryGetValue(leaf, out var partners))
                    {
                        partners = new HashSet<Component>();
                        abuttingPairs[leaf] = partners;
                    }
                    partners.Add(partner);
                }
            }
        }

        var detector = new GroupCollisionDetector();
        return !detector.CanPlaceGroup(
            chiplet, chiplet.PhysicalX + deltaX, chiplet.PhysicalY + deltaY,
            allComponents, preDragOverlapPartners: abuttingPairs);
    }

    /// <summary>Every non-group component in the collection that is not a member of the moved chiplet.</summary>
    private static List<Component> LeavesOutside(
        IEnumerable<Component> allComponents, HashSet<Component> memberSet)
    {
        var leaves = new List<Component>();
        foreach (var component in allComponents)
        {
            if (memberSet.Contains(component))
                continue;
            if (component is ComponentGroup group)
            {
                leaves.AddRange(group.GetAllComponentsRecursive().Where(c => c is not ComponentGroup));
            }
            else
            {
                leaves.Add(component);
            }
        }
        return leaves;
    }

    /// <summary>How far two 1-D intervals [pos, pos+length] overlap; negative when separated.</summary>
    private static double OverlapDepth(double posA, double lengthA, double posB, double lengthB) =>
        Math.Min(posA + lengthA, posB + lengthB) - Math.Max(posA, posB);

    /// <summary>True when the connection is a cross-chiplet edge-coupler link touching the chiplet.</summary>
    private static bool LinksChiplet(WaveguideConnection connection, ComponentGroup chiplet)
    {
        if (!ChipletInterfaceChecker.TryGetFacet(connection.StartPin, out var start)
            || !ChipletInterfaceChecker.TryGetFacet(connection.EndPin, out var end)
            || ReferenceEquals(start.Chiplet, end.Chiplet))
        {
            return false;
        }
        return ReferenceEquals(start.Chiplet, chiplet) || ReferenceEquals(end.Chiplet, chiplet);
    }
}
