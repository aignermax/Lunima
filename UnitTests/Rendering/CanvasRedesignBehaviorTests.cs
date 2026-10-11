using Avalonia;
using CAP.Avalonia.Controls.Rendering;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Rendering;

/// <summary>
/// Behavior of the canvas redesign helpers: adaptive grid spacing, the one-shot motion clock,
/// blocked-marker placement, the name-label level of detail and the overview group tag.
/// </summary>
public class CanvasRedesignBehaviorTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(0.02)]
    [InlineData(0.37)]
    [InlineData(8.0)]
    public void GridMinorSpacing_IsAPowerOfTen_NeverDenserThanTheMinimum(double zoom)
    {
        var spacing = AdaptiveGridSpacing.For(zoom);

        (spacing.MinorWorld * zoom).ShouldBeGreaterThanOrEqualTo(AdaptiveGridSpacing.MinMinorSpacingPx - 1e-9);
        (spacing.MinorWorld * zoom).ShouldBeLessThan(AdaptiveGridSpacing.MinMinorSpacingPx * 10 + 1e-9);
        Math.Log10(spacing.MinorWorld).ShouldBe(Math.Round(Math.Log10(spacing.MinorWorld)), 1e-9);
        spacing.MajorWorld.ShouldBe(spacing.MinorWorld * AdaptiveGridSpacing.MinorPerMajor, 1e-9);
        spacing.MinorOpacity.ShouldBeInRange(0, 1);
    }

    [Fact]
    public void Motion_Disabled_ReportsSettledStateAndRequestsNoFrame()
    {
        var motion = new CanvasMotion { IsEnabled = false };
        motion.BeginFrame();

        motion.SelectionGlow(Guid.NewGuid()).ShouldBe(1.0);
        motion.BlockedPulse(Guid.NewGuid()).ShouldBeNull();
        motion.NeedsNextFrame.ShouldBeFalse("an idle canvas must never schedule frames");
    }

    [Fact]
    public void Motion_NewSelection_StartsFadingIn_AndRequestsAFrame()
    {
        var motion = new CanvasMotion();
        motion.BeginFrame();

        motion.SelectionGlow(Guid.NewGuid()).ShouldBeLessThan(1.0);
        motion.NeedsNextFrame.ShouldBeTrue();
    }

    [Fact]
    public void Motion_BlockedPulse_StartsOnFirstSight()
    {
        var motion = new CanvasMotion();
        motion.BeginFrame();

        motion.BlockedPulse(Guid.NewGuid()).ShouldNotBeNull();
        motion.NeedsNextFrame.ShouldBeTrue();
    }

    [Fact]
    public void BlockedMarker_WithoutDrawnSegments_SitsMidwayBetweenThePins()
    {
        BlockedConnectionMarkerRenderer.PathMiddle(new Point(0, 0), new Point(100, 40), Array.Empty<PathSegment>())
            .ShouldBe(new Point(50, 20));
    }

    [Fact]
    public void BlockedMarker_OnARoutedPath_SitsOnTheMiddleSegment()
    {
        var segments = new List<PathSegment>
        {
            new StraightSegment(0, 0, 10, 0, 0),
            new StraightSegment(10, 0, 10, 30, 90),
            new StraightSegment(10, 30, 50, 30, 0),
        };

        BlockedConnectionMarkerRenderer.PathMiddle(new Point(0, 0), new Point(50, 30), segments)
            .ShouldBe(new Point(10, 15));
    }

    [Theory]
    [InlineData(100, 200, false, true)]
    [InlineData(20, 200, false, false)]
    [InlineData(20, 200, true, true)]
    public void NameLabel_IsDroppedWhenItsOwnerIsTooSmallOnScreen(double ownerWidth, double labelWidth, bool emphasized, bool expected)
    {
        ComponentRenderer.IsNameLegibleAtZoom(ownerWidth, labelWidth, zoom: 1.0, emphasized).ShouldBe(expected);
    }

    [Fact]
    public void GroupTag_GrowsAtOverviewZoom_AndHidesWhenWiderThanItsGroup()
    {
        ComponentGroupRenderer.LabelScale(1.0).ShouldBe(1.0);
        ComponentGroupRenderer.LabelScale(0.1).ShouldBe(9.0, 1e-9);

        var wide = GroupWithChildWidth(2000);
        var tag = ComponentGroupRenderer.CalculateLabelBounds(wide, zoom: 0.1);
        tag.Height.ShouldBe(18 * 9, 1e-9);

        var narrow = GroupWithChildWidth(50);
        ComponentGroupRenderer.CalculateLabelBounds(narrow, zoom: 0.1).Width.ShouldBe(0,
            "a tag wider than its group is left out (and is not hit-testable)");
        ComponentGroupRenderer.CalculateLabelBounds(narrow).Height.ShouldBe(18, "at 100 % the tag is unchanged");
    }

    private static ComponentGroup GroupWithChildWidth(double width)
    {
        var group = new ComponentGroup("G1");
        var child = UnitTests.TestComponentFactory.CreateStraightWaveGuide();
        child.PhysicalX = 0;
        child.PhysicalY = 0;
        child.WidthMicrometers = width;
        child.HeightMicrometers = 10;
        group.AddChild(child);
        return group;
    }
}
