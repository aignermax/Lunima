using CAP_Core.Routing.PinBreakout;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.PinBreakout;

/// <summary>Side choice per cluster wire and its repair into a crossing-free (monotone) fan.</summary>
public class BreakoutSideSelectorTests
{
    [Theory]
    [InlineData(100.0, 10.0, 0)]     // far end ahead: straight
    [InlineData(-50.0, 300.0, 1)]    // behind and to the higher side
    [InlineData(20.0, -300.0, -1)]   // mostly sideways to the lower side
    [InlineData(10.0, 10.0, 0)]      // diagonal counts as ahead
    public void PreferredSide_FollowsTheFarEnd(double forward, double lateral, int expected)
    {
        BreakoutSideSelector.PreferredSide(forward, lateral).ShouldBe(expected);
    }

    [Theory]
    [InlineData(new[] { -1, 0, 1 }, new[] { -1, 0, 1 })]
    [InlineData(new[] { 1, 1 }, new[] { 1, 1 })]
    [InlineData(new[] { -1, -1, 0 }, new[] { -1, -1, 0 })]
    public void MonotoneWishes_AreKept(int[] wishes, int[] expected)
    {
        BreakoutSideSelector.MakeMonotone(wishes).ShouldBe(expected);
    }

    [Fact]
    public void CrossedWishes_AreRepairedWithTheFewestChanges()
    {
        var sides = BreakoutSideSelector.MakeMonotone(new[] { 1, -1, -1 });

        sides.ShouldBe(new[] { -1, -1, -1 });
    }

    [Fact]
    public void RepairedSides_AreAlwaysNonDecreasing()
    {
        var random = new Random(1166);
        for (int trial = 0; trial < 200; trial++)
        {
            var wishes = Enumerable.Range(0, random.Next(1, 7)).Select(_ => random.Next(-1, 2)).ToArray();

            var sides = BreakoutSideSelector.MakeMonotone(wishes);

            sides.Length.ShouldBe(wishes.Length);
            for (int i = 1; i < sides.Length; i++)
                sides[i].ShouldBeGreaterThanOrEqualTo(sides[i - 1]);
        }
    }
}
