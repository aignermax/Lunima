using Avalonia.Media;
using CAP.Avalonia.Controls.Rendering;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Rendering;

/// <summary>
/// The canvas marks a group holding a broken frozen path at any depth, and draws a
/// blocked frozen path with the red dashed pen of a blocked top-level wire.
/// </summary>
public class GroupBrokenPathMarkerTests
{
    private static FrozenWaveguidePath Path(bool blocked = false, bool invalid = false)
    {
        var routed = new RoutedPath { IsBlockedFallback = blocked, IsInvalidGeometry = invalid };
        routed.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));
        return new FrozenWaveguidePath { Path = routed };
    }

    private static (ComponentGroup Outer, ComponentGroup Inner) Nested()
    {
        var inner = new ComponentGroup("INNER");
        inner.AddInternalPath(Path());
        var outer = new ComponentGroup("OUTER");
        outer.AddChild(inner);
        outer.AddInternalPath(Path());
        return (outer, inner);
    }

    [Fact]
    public void CleanHierarchy_NeedsNoMarker()
    {
        GroupBrokenPathMarkerRenderer.NeedsMarker(Nested().Outer).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void BrokenPathTwoLevelsDown_MarksTheTopLevelGroupAndItsOwner(bool blocked, bool invalid)
    {
        var (outer, inner) = Nested();
        inner.AddInternalPath(Path(blocked, invalid));

        GroupBrokenPathMarkerRenderer.NeedsMarker(outer).ShouldBeTrue();
        GroupBrokenPathMarkerRenderer.NeedsMarker(inner).ShouldBeTrue();
    }

    [Fact]
    public void BlockedFrozenPath_DrawsWithTheBlockedPen()
    {
        var blocked = ComponentGroupRenderer.SelectFrozenPathPen(Path(blocked: true));
        var clean = ComponentGroupRenderer.SelectFrozenPathPen(Path());

        ((ISolidColorBrush)blocked.Brush!).Color.ShouldBe(CanvasPalette.Danger);
        blocked.DashStyle.ShouldNotBeNull("blocked wires are dashed");
        ((ISolidColorBrush)clean.Brush!).Color.ShouldNotBe(CanvasPalette.Danger);
    }
}
