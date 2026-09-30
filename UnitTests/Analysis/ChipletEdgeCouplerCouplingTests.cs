using CAP_Core.Analysis;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.LightCalculation;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis;

/// <summary>
/// Geometry unit tests for <see cref="ChipletEdgeCouplerCoupling"/> (issue #1228) with
/// plain pins and groups — the same canvas-free fixture style as
/// <see cref="ChipletInterfaceCheckerTests"/>, because the coupling must key on exactly
/// the links the #1219 checker inspects.
/// </summary>
public class ChipletEdgeCouplerCouplingTests
{
    private const double CouplerWidth = 100;
    private const double CouplerHeight = 19;
    private const double PinY = 9.5;
    private const double Tolerance = 1e-12;

    [Fact]
    public void AlignedFacets_FactorIsExactlyOne()
    {
        var (link, _, _) = BuildLink(out _, out _);

        ChipletEdgeCouplerCoupling.FieldFactor(link).ShouldBe(1.0);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void LateralOffset_FieldFactorIsSqrtOfGaussianOverlap(double shiftY)
    {
        var (link, _, _) = BuildLink(out _, out var chipletB);
        chipletB.MoveGroup(0, shiftY);

        double expected = Math.Sqrt(ChipletEdgeCouplerCoupling.PowerCouplingForOffset(shiftY));

        ChipletEdgeCouplerCoupling.FieldFactor(link).ShouldBe(expected, Tolerance);
    }

    [Theory]
    [InlineData(179.0, false)] // exactly 1° off the antiparallel ideal — still facing
    [InlineData(178.9, true)]  // past the 1° facing tolerance — no coupling
    [InlineData(90.0, true)]   // quarter turn — clearly not facing
    public void FacingBoundary_NotFacingCouplesNothing(double endPinAngle, bool expectZero)
    {
        var (link, _, endCoupler) = BuildLink(out _, out _);
        endCoupler.PhysicalPins[0].AngleDegrees = endPinAngle;

        double factor = ChipletEdgeCouplerCoupling.FieldFactor(link);

        if (expectZero)
        {
            factor.ShouldBe(0.0);
        }
        else
        {
            factor.ShouldBeGreaterThan(0.0);
        }
    }

    [Fact]
    public void SameChipletLink_FactorIsOne()
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

        ChipletEdgeCouplerCoupling.FieldFactor(link).ShouldBe(1.0);
    }

    [Fact]
    public void NonEdgeCouplerLink_FactorIsOne()
    {
        var (link, startCoupler, endCoupler) = BuildLink(out _, out _);
        startCoupler.TemplateName = "Grating Coupler";
        endCoupler.TemplateName = "Grating Coupler";

        ChipletEdgeCouplerCoupling.FieldFactor(link).ShouldBe(1.0);
    }

    [Fact]
    public void UngroupedEdgeCouplers_FactorIsOne()
    {
        var startCoupler = CreateEdgeCoupler("a_ec", 0, 0, CouplerWidth, PinY, 0);
        var endCoupler = CreateEdgeCoupler("b_ec", CouplerWidth, 0, 0, PinY, 180);
        var link = new WaveguideConnection
        {
            StartPin = startCoupler.PhysicalPins[0],
            EndPin = endCoupler.PhysicalPins[0],
        };

        ChipletEdgeCouplerCoupling.FieldFactor(link).ShouldBe(1.0);
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(ChipletEdgeCouplerCoupling.ModeWaistMicrometers, 0.36787944117144233)] // exp(-1) at d = w0
    public void PowerCouplingForOffset_GaussianModeOverlap(double offset, double expected)
    {
        ChipletEdgeCouplerCoupling.PowerCouplingForOffset(offset).ShouldBe(expected, Tolerance);
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
