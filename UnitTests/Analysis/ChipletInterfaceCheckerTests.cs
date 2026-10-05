using CAP_Core.Analysis;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.LightCalculation;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis;

/// <summary>
/// Geometry unit tests for <see cref="ChipletInterfaceChecker"/> (issue #1219) with plain
/// pins and groups — no canvas. Each tolerance boundary (facing 1°, lateral 0.5 µm,
/// edge 1 µm) is exercised from both sides.
/// </summary>
public class ChipletInterfaceCheckerTests
{
    private const double CouplerWidth = 100;
    private const double CouplerHeight = 19;
    private const double PinY = 9.5;
    private const double WavelengthNm = 1550;

    private readonly ChipletInterfaceChecker _checker = new();

    [Fact]
    public void FacingCoincidentOnEdge_NoIssues()
    {
        var (link, _, _) = BuildLink(out _, out _);

        _checker.Check(new[] { link }, WavelengthNm).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(179.0, false)] // exactly 1° off the antiparallel ideal — inside tolerance
    [InlineData(178.9, true)]  // past the 1° facing tolerance
    [InlineData(90.0, true)]   // quarter turn — clearly not facing
    public void FacingToleranceBoundary(double endPinAngle, bool expectIssue)
    {
        var (link, _, endCoupler) = BuildLink(out _, out _);
        endCoupler.PhysicalPins[0].AngleDegrees = endPinAngle;

        var issues = _checker.Check(new[] { link }, WavelengthNm);

        issues.Count(i => i.Type == DesignIssueType.ChipletInterfaceNotFacing)
            .ShouldBe(expectIssue ? 1 : 0);
    }

    [Fact]
    public void NotFacing_LateralCheckIsSkipped()
    {
        var (link, _, endCoupler) = BuildLink(out _, out _);
        endCoupler.PhysicalPins[0].AngleDegrees = 90.0;

        var issues = _checker.Check(new[] { link }, WavelengthNm);

        issues.ShouldContain(i => i.Type == DesignIssueType.ChipletInterfaceNotFacing);
        issues.ShouldNotContain(i => i.Type == DesignIssueType.ChipletInterfaceLateralOffset,
            "a perpendicular offset is meaningless between crossed axes");
    }

    [Theory]
    [InlineData(0.5, false)] // exactly at the lateral limit — allowed
    [InlineData(0.6, true)]  // past the 0.5 µm limit
    [InlineData(2.0, true)]  // clearly misaligned
    public void LateralToleranceBoundary(double shiftY, bool expectIssue)
    {
        var (link, _, _) = BuildLink(out _, out var chipletB);
        chipletB.MoveGroup(0, shiftY);

        var issues = _checker.Check(new[] { link }, WavelengthNm);

        var lateral = issues.Where(i => i.Type == DesignIssueType.ChipletInterfaceLateralOffset).ToList();
        lateral.Count.ShouldBe(expectIssue ? 1 : 0);
        if (expectIssue)
        {
            lateral[0].Description.ShouldContain("Chiplet A");
            lateral[0].Description.ShouldContain("Chiplet B");
            lateral[0].Description.ShouldContain("µm");
        }
    }

    [Fact]
    public void LateralOffset_MessageCarriesInvariantCultureValue()
    {
        var (link, _, _) = BuildLink(out _, out var chipletB);
        chipletB.MoveGroup(0, 2.0);

        var issue = _checker.Check(new[] { link }, WavelengthNm)
            .Single(i => i.Type == DesignIssueType.ChipletInterfaceLateralOffset);

        issue.Description.ShouldContain("2.00");
    }

    [Theory]
    [InlineData(1.0, false)] // exactly at the edge tolerance — allowed
    [InlineData(1.1, true)]  // past the 1 µm edge tolerance
    [InlineData(20.0, true)] // deep inside the chiplet
    public void EdgeToleranceBoundary(double inset, bool expectIssue)
    {
        var (link, _, _) = BuildLink(out _, out _);
        // A spacer holds chiplet B's boundary at the die edge while the coupler
        // moves inward — other content defines the chiplet extents.
        MoveEndCouplerInside(link, inset);

        var issues = _checker.Check(new[] { link }, WavelengthNm);

        issues.Count(i => i.Type == DesignIssueType.ChipletInterfaceOffEdge)
            .ShouldBe(expectIssue ? 1 : 0);
    }

    [Fact]
    public void OffEdge_MessageNamesChipletPinAndDistance()
    {
        var (link, _, _) = BuildLink(out _, out _);
        MoveEndCouplerInside(link, 20.0);

        var issue = _checker.Check(new[] { link }, WavelengthNm)
            .Single(i => i.Type == DesignIssueType.ChipletInterfaceOffEdge);

        issue.Description.ShouldContain("Chiplet B");
        issue.Description.ShouldContain("b_ec.fiber");
        issue.Description.ShouldContain("20.0");
    }

    [Theory]
    [InlineData(0.0, false)] // butt-coupled — no divergence loss
    [InlineData(1.0, false)] // 0.05 dB at 1550 nm — far inside the 1 dB budget
    [InlineData(4.0, false)] // 0.76 dB — still inside
    [InlineData(5.0, true)]  // 1.14 dB — past the 1 dB budget (boundary ≈ 4.64 µm)
    [InlineData(20.0, true)] // 7.64 dB — clearly gapped
    public void GapLossBoundary(double gapMicrometers, bool expectIssue)
    {
        var (link, _, _) = BuildLink(out _, out var chipletB);
        chipletB.MoveGroup(gapMicrometers, 0); // along the link axis — pure gap, no offset

        var issues = _checker.Check(new[] { link }, WavelengthNm);

        issues.Count(i => i.Type == DesignIssueType.ChipletInterfaceGapLoss)
            .ShouldBe(expectIssue ? 1 : 0);
    }

    [Fact]
    public void OverlappingFacets_NoGapIssue()
    {
        var (link, _, _) = BuildLink(out _, out var chipletB);
        chipletB.MoveGroup(-5.0, 0); // overlapping facets — a placement concern, not a gap

        var issues = _checker.Check(new[] { link }, WavelengthNm);

        issues.ShouldNotContain(i => i.Type == DesignIssueType.ChipletInterfaceGapLoss);
    }

    [Fact]
    public void NotFacing_GapCheckIsSkipped()
    {
        var (link, _, endCoupler) = BuildLink(out _, out var chipletB);
        chipletB.MoveGroup(20.0, 0);
        endCoupler.PhysicalPins[0].AngleDegrees = 90.0;

        var issues = _checker.Check(new[] { link }, WavelengthNm);

        issues.ShouldContain(i => i.Type == DesignIssueType.ChipletInterfaceNotFacing);
        issues.ShouldNotContain(i => i.Type == DesignIssueType.ChipletInterfaceGapLoss,
            "an axial gap is meaningless between crossed axes");
    }

    [Fact]
    public void GapLoss_MessageNamesChipletsGapAndLoss()
    {
        var (link, _, _) = BuildLink(out _, out var chipletB);
        chipletB.MoveGroup(10.0, 0); // 3.43 dB at 1550 nm

        var issue = _checker.Check(new[] { link }, WavelengthNm)
            .Single(i => i.Type == DesignIssueType.ChipletInterfaceGapLoss);

        issue.Description.ShouldContain("Chiplet A");
        issue.Description.ShouldContain("Chiplet B");
        issue.Description.ShouldContain("10.00 µm");
        issue.Description.ShouldContain("3.43 dB");
    }

    [Fact]
    public void SameChipletLink_IsSkipped()
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

        _checker.Check(new[] { link }, WavelengthNm).ShouldBeEmpty();
    }

    [Fact]
    public void NonEdgeCouplerLink_IsSkipped()
    {
        var (link, startCoupler, endCoupler) = BuildLink(out _, out _);
        startCoupler.TemplateName = "Grating Coupler";
        endCoupler.TemplateName = "Grating Coupler";

        _checker.Check(new[] { link }, WavelengthNm).ShouldBeEmpty();
    }

    [Fact]
    public void UngroupedEdgeCouplers_AreSkipped()
    {
        var startCoupler = CreateEdgeCoupler("a_ec", 0, 0, CouplerWidth, PinY, 0);
        var endCoupler = CreateEdgeCoupler("b_ec", CouplerWidth, 0, 0, PinY, 180);
        var link = new WaveguideConnection
        {
            StartPin = startCoupler.PhysicalPins[0],
            EndPin = endCoupler.PhysicalPins[0],
        };

        _checker.Check(new[] { link }, WavelengthNm).ShouldBeEmpty();
    }

    [Fact]
    public void NestedGroups_ChipletIsTheTopLevelGroup()
    {
        var startCoupler = CreateEdgeCoupler("a_ec", 0, 0, CouplerWidth, PinY, 0);
        var endCoupler = CreateEdgeCoupler("b_ec", CouplerWidth, 0, 0, PinY, 180);
        var inner = new ComponentGroup("Inner Stage");
        inner.AddChild(endCoupler);
        var outer = new ComponentGroup("Receiver Die");
        outer.AddChild(inner);
        var chipletA = new ComponentGroup("Chiplet A");
        chipletA.AddChild(startCoupler);
        var link = new WaveguideConnection
        {
            StartPin = startCoupler.PhysicalPins[0],
            EndPin = endCoupler.PhysicalPins[0],
        };
        inner.MoveGroup(0, 2.0); // lateral offset, reported against the OUTER die

        var issue = _checker.Check(new[] { link }, WavelengthNm)
            .Single(i => i.Type == DesignIssueType.ChipletInterfaceLateralOffset);

        issue.Description.ShouldContain("Receiver Die");
        issue.Description.ShouldNotContain("Inner Stage");
    }

    /// <summary>
    /// Builds the aligned reference link: chiplet A's coupler facet faces east out of its
    /// right edge, chiplet B's facet faces west out of its left edge, pins coincident.
    /// </summary>
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

    /// <summary>
    /// Moves chiplet B's coupler <paramref name="inset"/> µm inward (away from the shared
    /// boundary) while a spacer at the coupler's original position keeps the group bounds
    /// at the die edge — other chiplet content defines the extents, the facet no longer
    /// reaches them.
    /// </summary>
    private static void MoveEndCouplerInside(WaveguideConnection link, double inset)
    {
        var endCoupler = link.EndPin.ParentComponent;
        var chipletB = (ComponentGroup)endCoupler.ParentGroup!;
        double dieEdgeX = endCoupler.PhysicalX;
        endCoupler.PhysicalX += inset;
        var spacer = CreateEdgeCoupler("b_spacer", dieEdgeX, 0, 0, PinY, 180);
        spacer.WidthMicrometers = 1;
        chipletB.AddChild(spacer); // recomputes the bounds: die edge stays, coupler sits inside
    }

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
