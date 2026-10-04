using CAP_Core.Analysis;
using CAP_Core.Components;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis;

/// <summary>
/// Geometry unit tests for <see cref="ChipletLinkAligner"/> (issue #1248) with plain
/// pins and groups — no canvas. Offset-only, gap-only and offset+gap links must align
/// to within 1e-6 µm (checker-clean, field factor exactly 1); non-facing facets, a
/// blocking component and a third-chiplet link the move would break must refuse.
/// </summary>
public class ChipletLinkAlignerTests
{
    private const double CouplerWidth = 100;
    private const double CouplerHeight = 19;
    private const double PinY = 9.5;
    private const double WavelengthNm = 1550;
    private const double AlignmentTolerance = 1e-6;

    private readonly ChipletLinkAligner _aligner = new();
    private readonly ChipletInterfaceChecker _checker = new();

    [Fact]
    public void OffsetOnly_AlignsToZero()
    {
        var (link, _, _) = BuildLink(out _, out var chipletB);
        chipletB.MoveGroup(0, 2.0);

        AlignAndVerify(link, chipletB);
    }

    [Fact]
    public void GapOnly_AlignsToZero()
    {
        var (link, _, _) = BuildLink(out _, out var chipletB);
        chipletB.MoveGroup(10.0, 0);

        AlignAndVerify(link, chipletB);
    }

    [Fact]
    public void OffsetPlusGap_AlignsToZero()
    {
        var (link, _, _) = BuildLink(out _, out var chipletB);
        chipletB.MoveGroup(6.0, 1.5);

        AlignAndVerify(link, chipletB);
    }

    /// <summary>Plans, applies, and asserts the link is checker-clean with field factor 1.</summary>
    private void AlignAndVerify(WaveguideConnection link, ComponentGroup chipletB)
    {
        var components = new Component[] { StartChipletOf(link), chipletB };
        _aligner.TryPlan(link, new[] { link }, components, WavelengthNm, out var plan, out _)
            .ShouldBeTrue();
        plan!.Chiplet.ShouldBe(chipletB);

        chipletB.MoveGroup(plan.DeltaX, plan.DeltaY);

        var (startX, startY) = link.StartPin.GetAbsolutePosition();
        var (endX, endY) = link.EndPin.GetAbsolutePosition();
        double angle = link.StartPin.GetAbsoluteAngle();
        ChipletInterfaceChecker.LateralOffset(startX, startY, endX, endY, angle)
            .ShouldBeLessThan(AlignmentTolerance);
        ChipletInterfaceChecker.AxialGap(startX, startY, endX, endY, angle)
            .ShouldBeLessThan(AlignmentTolerance);
        _checker.Check(new[] { link }, WavelengthNm).ShouldBeEmpty();
        ChipletEdgeCouplerCoupling.FieldFactor(link, WavelengthNm).ShouldBe(1.0);
    }

    [Fact]
    public void NotFacing_Refuses()
    {
        var (link, _, endCoupler) = BuildLink(out _, out _);
        endCoupler.PhysicalPins[0].AngleDegrees = 90.0;

        _aligner.TryPlan(link, new[] { link }, BothChiplets(link), WavelengthNm, out var plan, out var refusal)
            .ShouldBeFalse();
        refusal.ShouldBe(ChipletAlignmentRefusal.NotFacing);
        plan.ShouldBeNull();
    }

    [Fact]
    public void AlreadyAligned_Refuses()
    {
        var (link, _, _) = BuildLink(out _, out _);

        _aligner.TryPlan(link, new[] { link }, BothChiplets(link), WavelengthNm, out _, out var refusal)
            .ShouldBeFalse();
        refusal.ShouldBe(ChipletAlignmentRefusal.AlreadyAligned);
    }

    [Fact]
    public void SameChipletLink_Refuses()
    {
        var startCoupler = CreateEdgeCoupler("a_ec", 0, 0, CouplerWidth, PinY, 0);
        var endCoupler = CreateEdgeCoupler("b_ec", CouplerWidth, 0, 0, PinY, 180);
        var singleChiplet = new ComponentGroup("Single Chiplet");
        singleChiplet.AddChild(startCoupler);
        singleChiplet.AddChild(endCoupler);
        var link = new WaveguideConnection
        {
            StartPin = startCoupler.PhysicalPins[0],
            EndPin = endCoupler.PhysicalPins[0],
        };

        _aligner.TryPlan(link, new[] { link }, new Component[] { singleChiplet }, WavelengthNm, out _, out var refusal)
            .ShouldBeFalse();
        refusal.ShouldBe(ChipletAlignmentRefusal.NotCrossChipletLink);
    }

    [Fact]
    public void BlockingComponent_Refuses_AndDoesNotMove()
    {
        var (link, _, _) = BuildLink(out _, out var chipletB);
        chipletB.MoveGroup(0, 2.0); // the fix would move chiplet B back down by 2 µm
        double blockedX = chipletB.PhysicalX;
        double blockedY = chipletB.PhysicalY;

        // A blocker sitting where chiplet B's coupler would land (its original position).
        var blocker = CreateEdgeCoupler("blocker", CouplerWidth + 10, 5, 0, PinY, 180);
        blocker.TemplateName = "Straight Waveguide";

        var components = new Component[] { StartChipletOf(link), chipletB, blocker };
        _aligner.TryPlan(link, new[] { link }, components, WavelengthNm, out var plan, out var refusal)
            .ShouldBeFalse();
        refusal.ShouldBe(ChipletAlignmentRefusal.Overlap);
        plan.ShouldBeNull();
        chipletB.PhysicalX.ShouldBe(blockedX, "a refused plan must not move the chiplet");
        chipletB.PhysicalY.ShouldBe(blockedY);
    }

    [Fact]
    public void ButtCouplingItself_IsNotTreatedAsOverlap()
    {
        // The facet pair abuts exactly at the target — touching must not count as blocking.
        var (link, _, _) = BuildLink(out _, out var chipletB);
        chipletB.MoveGroup(10.0, 0);

        _aligner.TryPlan(link, new[] { link }, BothChiplets(link), WavelengthNm, out _, out var refusal)
            .ShouldBeTrue($"abutting facets must be allowed, got refusal {refusal}");
    }

    [Fact]
    public void ThirdChipletLinkBrokenByTheMove_Refuses_AndDoesNotMove()
    {
        var (link, _, _) = BuildLink(out _, out var chipletB);
        // Chiplet B's second edge coupler faces east out of B's right side…
        var secondCoupler = CreateEdgeCoupler("b_ec2", 2 * CouplerWidth, 0, CouplerWidth, PinY, 0);
        chipletB.AddChild(secondCoupler);
        // …and chiplet C's coupler faces west into it.
        var cCoupler = CreateEdgeCoupler("c_ec", 3 * CouplerWidth, 0, 0, PinY, 180);
        var chipletC = new ComponentGroup("Chiplet C");
        chipletC.AddChild(cCoupler);
        var linkBC = new WaveguideConnection
        {
            StartPin = secondCoupler.PhysicalPins[0],
            EndPin = cCoupler.PhysicalPins[0],
        };

        // Misalign A–B laterally, then place C so ITS link is clean in the shifted
        // position — the A–B fix would drag B back down and break B–C.
        chipletB.MoveGroup(0, 3.0);
        chipletC.MoveGroup(0, 3.0);
        _checker.Check(new[] { linkBC }, WavelengthNm).ShouldBeEmpty("B–C starts clean");

        double blockedY = chipletB.PhysicalY;
        var connections = new[] { link, linkBC };
        var components = new Component[] { StartChipletOf(link), chipletB, chipletC };

        _aligner.TryPlan(link, connections, components, WavelengthNm, out var plan, out var refusal)
            .ShouldBeFalse();
        refusal.ShouldBe(ChipletAlignmentRefusal.WouldBreakOtherLink);
        plan.ShouldBeNull();
        chipletB.PhysicalY.ShouldBe(blockedY, "a refused plan must not move the chiplet");
        _checker.Check(new[] { linkBC }, WavelengthNm).ShouldBeEmpty("B–C stays clean after the refusal");
    }

    [Fact]
    public void ThirdChipletLinkThatStaysClean_DoesNotRefuse()
    {
        var (link, _, _) = BuildLink(out _, out var chipletB);
        var secondCoupler = CreateEdgeCoupler("b_ec2", 2 * CouplerWidth, 0, CouplerWidth, PinY, 0);
        chipletB.AddChild(secondCoupler);
        var cCoupler = CreateEdgeCoupler("c_ec", 3 * CouplerWidth, 0, 0, PinY, 180);
        var chipletC = new ComponentGroup("Chiplet C");
        chipletC.AddChild(cCoupler);
        var linkBC = new WaveguideConnection
        {
            StartPin = secondCoupler.PhysicalPins[0],
            EndPin = cCoupler.PhysicalPins[0],
        };

        // Pure-gap A–B misalignment along x: the fix moves B back west; B–C's overlapping
        // facets (gap clamps to 0) stay clean throughout.
        chipletB.MoveGroup(10.0, 0);
        var connections = new[] { link, linkBC };
        var components = new Component[] { StartChipletOf(link), chipletB, chipletC };

        _aligner.TryPlan(link, connections, components, WavelengthNm, out _, out var refusal)
            .ShouldBeTrue($"a fix that keeps B–C clean must be allowed, got refusal {refusal}");
    }

    /// <summary>The aligned reference link: coincident facing facets across two chiplets.</summary>
    private (WaveguideConnection Link, Component StartCoupler, Component EndCoupler) BuildLink(
        out ComponentGroup chipletA, out ComponentGroup chipletB)
    {
        var startCoupler = CreateEdgeCoupler("a_ec", 0, 0, CouplerWidth, PinY, 0);
        var endCoupler = CreateEdgeCoupler("b_ec", CouplerWidth, 0, 0, PinY, 180);
        chipletA = new ComponentGroup("Chiplet A");
        chipletA.AddChild(startCoupler);
        chipletB = new ComponentGroup("Chiplet B");
        chipletB.AddChild(endCoupler);
        var link = new WaveguideConnection
        {
            StartPin = startCoupler.PhysicalPins[0],
            EndPin = endCoupler.PhysicalPins[0],
        };
        return (link, startCoupler, endCoupler);
    }

    private static ComponentGroup StartChipletOf(WaveguideConnection link) =>
        (ComponentGroup)link.StartPin.ParentComponent.ParentGroup!;

    private Component[] BothChiplets(WaveguideConnection link) =>
        new Component[] { StartChipletOf(link), (ComponentGroup)link.EndPin.ParentComponent.ParentGroup! };

    private static Component CreateEdgeCoupler(
        string identifier, double x, double y, double pinOffsetX, double pinOffsetY, double pinAngle)
    {
        var pin = new PhysicalPin
        {
            Name = "fiber",
            OffsetXMicrometers = pinOffsetX,
            OffsetYMicrometers = pinOffsetY,
            AngleDegrees = pinAngle,
        };
        return new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "demo.io",
            "",
            new Part[1, 1] { { new Part() } },
            -1,
            identifier,
            new DiscreteRotation(),
            new List<PhysicalPin> { pin })
        {
            WidthMicrometers = CouplerWidth,
            HeightMicrometers = CouplerHeight,
            PhysicalX = x,
            PhysicalY = y,
            TemplateName = "Edge Coupler",
        };
    }
}
