using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Rung-6 DRC-lite acceptance (issue #1219): the chiplet-interface rule fires on the
/// <see cref="ChipletEdgeCouplerJourneyDesign"/> (#1214) exactly when the butt-coupled
/// edge-coupler pair is misaligned — laterally shifted, rotated away, or moved off the
/// chiplet edge — and stays silent on the journey as built. Validation runs through the
/// real Design Checks panel (<see cref="DesignValidationViewModel"/>), wired like
/// MainViewModel.RunDesignChecks.
/// </summary>
public class ChipletInterfaceDrcJourneyTests
{
    [Fact]
    public void AsBuilt_NoChipletInterfaceIssues()
    {
        var design = ChipletEdgeCouplerJourneyDesign.BuildComposed();

        var panel = RunValidation(design.Canvas);

        ChipletInterfaceIssues(panel).ShouldBeEmpty(
            "the journey stays clean: " + Describe(panel));
    }

    [Fact]
    public void ChipletBShifted2MicrometersPerpendicular_OneLateralOffsetIssue()
    {
        var design = ChipletEdgeCouplerJourneyDesign.BuildComposed();
        design.ChipletB.MoveGroup(0, 2.0);

        var panel = RunValidation(design.Canvas);

        var issue = ChipletInterfaceIssues(panel).ShouldHaveSingleItem(Describe(panel));
        issue.Type.ShouldBe(DesignIssueType.ChipletInterfaceLateralOffset);
        issue.Description.ShouldContain(ChipletEdgeCouplerJourneyDesign.ChipletAName);
        issue.Description.ShouldContain(ChipletEdgeCouplerJourneyDesign.ChipletBName);
        issue.Description.ShouldContain("2");
    }

    [Fact]
    public void ChipletBEdgeCouplerRotated90_FacingIssue()
    {
        var design = ChipletEdgeCouplerJourneyDesign.BuildComposed();
        var edgeCouplerB = ChipletEdgeCouplerJourneyDesign.ExposedPin(design.ChipletB, "b_ec_fiber")
            .ParentComponent;
        ComponentPoseTransform.Rotate90CounterClockwise(edgeCouplerB);

        var panel = RunValidation(design.Canvas);

        ChipletInterfaceIssues(panel).ShouldContain(
            i => i.Type == DesignIssueType.ChipletInterfaceNotFacing,
            "a quarter-turned facet cannot butt against its counterpart: " + Describe(panel));
        ChipletInterfaceIssues(panel).ShouldNotContain(
            i => i.Type == DesignIssueType.ChipletInterfaceLateralOffset,
            "lateral offset is only measured between facing couplers: " + Describe(panel));
    }

    [Fact]
    public void EdgeCouplerMoved20MicrometersInsideChiplet_OffEdgeIssue()
    {
        var design = ChipletEdgeCouplerJourneyDesign.BuildComposed();
        // Model-level fault injection: the facet retreats 20 µm into chiplet B while the
        // group's outline keeps the chiplet extents it was composed with.
        var edgeCouplerB = ChipletEdgeCouplerJourneyDesign.ExposedPin(design.ChipletB, "b_ec_fiber")
            .ParentComponent;
        edgeCouplerB.PhysicalX += 20.0;

        var panel = RunValidation(design.Canvas);

        var issue = ChipletInterfaceIssues(panel).ShouldHaveSingleItem(Describe(panel));
        issue.Type.ShouldBe(DesignIssueType.ChipletInterfaceOffEdge);
        issue.Description.ShouldContain(ChipletEdgeCouplerJourneyDesign.ChipletBName);
        issue.Description.ShouldContain("20");
    }

    private static List<DesignIssue> ChipletInterfaceIssues(DesignValidationViewModel panel) =>
        panel.Issues.Where(i => i.Type is DesignIssueType.ChipletInterfaceNotFacing
                or DesignIssueType.ChipletInterfaceLateralOffset
                or DesignIssueType.ChipletInterfaceOffEdge)
            .ToList();

    /// <summary>Runs Design Validation wired like MainViewModel.RunDesignChecks (#936).</summary>
    private static DesignValidationViewModel RunValidation(DesignCanvasViewModel canvas)
    {
        var externalPortPins = canvas.Components
            .SelectMany(vm => vm.Component is ComponentGroup group
                ? group.ExternalPins
                : Enumerable.Empty<GroupPin>())
            .Select(pin => pin.InternalPin!)
            .ToList();
        var panel = new DesignValidationViewModel();
        panel.RunValidation(
            canvas.ConnectionManager.Connections,
            allComponents: canvas.Components.Select(vm => vm.Component),
            processLockActive: false,
            externalPortPins: externalPortPins);
        return panel;
    }

    private static string Describe(DesignValidationViewModel panel) =>
        string.Join(" | ", panel.Issues.Select(i => $"{i.Type}: {i.Description}"));
}
