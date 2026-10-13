using CAP_Core.Components.Core;

namespace CAP_Core.Routing.PinBreakout;

/// <summary>
/// A wire that takes part in breakout planning: its two pins and the bend radius the router
/// will use for it (connection radius raised to the process floor).
/// </summary>
/// <param name="Start">The wire's start pin.</param>
/// <param name="End">The wire's end pin.</param>
/// <param name="BendRadiusMicrometers">Effective minimum bend radius (µm) of the wire.</param>
public readonly record struct BreakoutWire(PhysicalPin Start, PhysicalPin End, double BendRadiusMicrometers);

/// <summary>
/// The planned breakout of one pin of a tight cluster: the fixed S-bend geometry from the
/// pin to its breakout anchor (where the grid route takes over), and the straight lead
/// beyond the anchor that is kept free of sibling wires so the wire can arrive straight.
/// </summary>
public sealed class PinBreakoutPlan
{
    /// <summary>Position tolerance (µm) for "the pin has not moved since planning".</summary>
    private const double PinMoveToleranceMicrometers = 1e-3;

    /// <summary>Heading tolerance (degrees) for "the pin has not rotated since planning".</summary>
    private const double PinTurnToleranceDegrees = 1e-3;

    /// <summary>Creates a plan for <paramref name="pin"/>.</summary>
    public PinBreakoutPlan(PhysicalPin pin, IReadOnlyList<PathSegment> breakoutSegments,
        IReadOnlyList<PathSegment> leadSegments, double bendRadiusMicrometers, PinCluster cluster)
    {
        Pin = pin;
        Cluster = cluster;
        BreakoutSegments = breakoutSegments;
        LeadSegments = leadSegments;
        BendRadiusMicrometers = bendRadiusMicrometers;
        (PinX, PinY) = pin.GetAbsolutePosition();
        HeadingDegrees = pin.GetAbsoluteAngle();
        (AnchorX, AnchorY) = breakoutSegments.Count > 0 ? breakoutSegments[^1].EndPoint : (PinX, PinY);
        AnchorHeadingDegrees = breakoutSegments.Count > 0 ? breakoutSegments[^1].EndAngleDegrees : HeadingDegrees;
    }

    /// <summary>The planned pin.</summary>
    public PhysicalPin Pin { get; }

    /// <summary>The cluster the pin breaks out of (its sibling pins share the face).</summary>
    public PinCluster Cluster { get; }

    /// <summary>Pin → anchor geometry (S-bend plus straight), in outward direction.</summary>
    public IReadOnlyList<PathSegment> BreakoutSegments { get; }

    /// <summary>The straight lead reserved beyond the anchor (outward direction).</summary>
    public IReadOnlyList<PathSegment> LeadSegments { get; }

    /// <summary>Radius (µm) of the breakout arcs.</summary>
    public double BendRadiusMicrometers { get; }

    /// <summary>Pin X (µm) at planning time.</summary>
    public double PinX { get; }

    /// <summary>Pin Y (µm) at planning time.</summary>
    public double PinY { get; }

    /// <summary>Outward pin heading (degrees) at planning time.</summary>
    public double HeadingDegrees { get; }

    /// <summary>Heading (degrees) the breakout leaves its anchor with — the pin heading, or a lane heading after a turn.</summary>
    public double AnchorHeadingDegrees { get; }

    /// <summary>Anchor X (µm): where the grid route starts or ends.</summary>
    public double AnchorX { get; }

    /// <summary>Anchor Y (µm): where the grid route starts or ends.</summary>
    public double AnchorY { get; }

    /// <summary>Grid reservation key of this plan (the pin's identity).</summary>
    public Guid ReservationId => Pin.PinId;

    /// <summary>Everything the plan reserves on the grid: breakout plus lead.</summary>
    public IEnumerable<PathSegment> ReservedSegments => BreakoutSegments.Concat(LeadSegments);

    /// <summary>True while the pin still sits where (and faces where) the plan was made.</summary>
    public bool MatchesCurrentPin()
    {
        var (x, y) = Pin.GetAbsolutePosition();
        double turn = Math.Abs(Pin.GetAbsoluteAngle() - HeadingDegrees);
        return Math.Abs(x - PinX) <= PinMoveToleranceMicrometers
            && Math.Abs(y - PinY) <= PinMoveToleranceMicrometers
            && turn <= PinTurnToleranceDegrees;
    }

    /// <summary>
    /// A stand-in pin at the anchor facing the pin's outward heading: the grid route treats
    /// the anchor exactly like a pin of a component placed there.
    /// </summary>
    public PhysicalPin CreateAnchorPin()
    {
        var anchorPin = new PhysicalPin
        {
            Name = Pin.Name,
            AngleDegrees = AnchorHeadingDegrees,
            WaveguideWidthMicrometers = Pin.WaveguideWidthMicrometers,
            Layer = Pin.Layer,
        };
        var anchor = new Component(new(), new(), "", "", new Part[1, 1], 0,
            AnchorComponentIdentifier, DiscreteRotation.R0, new List<PhysicalPin> { anchorPin })
        {
            PhysicalX = AnchorX,
            PhysicalY = AnchorY,
        };
        return anchor.PhysicalPins[0];
    }

    /// <summary>Identifier of the stand-in component carrying an anchor pin.</summary>
    public const string AnchorComponentIdentifier = "pin-breakout-anchor";
}
