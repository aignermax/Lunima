using Avalonia;
using CAP.Avalonia.Controls.TourAnchor;

namespace UnitTests.Onboarding.FirstStepsTutorial;

public class GuidePointerMotionTests
{
    [Fact]
    public void Pointer_ApproachesTarget_ThenShowsClickAtTarget()
    {
        var target = new Point(200, 100);
        var start = GuidePointerMotion.Sample(target, 0, false);
        var middle = GuidePointerMotion.Sample(target, .45, false);
        var click = GuidePointerMotion.Sample(target, 1.1, false);
        Assert.True(DistanceSquared(middle.Position, target) < DistanceSquared(start.Position, target));
        Assert.Equal(target, click.Position);
        Assert.True(click.ClickOpacity > 0);
        Assert.True(click.ClickRadius > 0);
    }

    private static double DistanceSquared(Point a, Point b) =>
        Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2);

    [Fact]
    public void ReducedMotion_UsesStationaryCueAtTarget()
    {
        var target = new Point(55, 60);
        var first = GuidePointerMotion.Sample(target, 0, true);
        Assert.Equal(target, first.Position);
        Assert.Equal(first, GuidePointerMotion.Sample(target, 2.8, true));
    }

    [Fact]
    public void Demonstration_RepeatsWithoutAccumulatingDrift()
    {
        var target = new Point(300, 100);
        var first = GuidePointerMotion.Sample(target, 1.1, false);
        var repeated = GuidePointerMotion.Sample(target, 4.7, false);
        Assert.Equal(first.Position, repeated.Position);
        Assert.Equal(first.ClickOpacity, repeated.ClickOpacity, 6);
        Assert.Equal(first.ClickRadius, repeated.ClickRadius, 6);
        Assert.Equal(target, repeated.Position);
    }
}
