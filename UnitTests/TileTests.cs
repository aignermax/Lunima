using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Tiles;
using CAP_Core.Helpers;
using Shouldly;
using Xunit;

namespace UnitTests
{
    public class TileTests
    {
        [Fact]
        public void CalculateSideShiftingTests()
        {
            RectSide.Right.RotateSideCounterClockwise( DiscreteRotation.R0).ShouldBe(RectSide.Right);
            RectSide.Right.RotateSideCounterClockwise( DiscreteRotation.R90).ShouldBe(RectSide.Up);
            RectSide.Right.RotateSideCounterClockwise( DiscreteRotation.R180).ShouldBe(RectSide.Left);
            RectSide.Right.RotateSideCounterClockwise( DiscreteRotation.R270).ShouldBe(RectSide.Down);
            RectSide.Down.RotateSideCounterClockwise( DiscreteRotation.R0).ShouldBe(RectSide.Down);
            RectSide.Down.RotateSideCounterClockwise( DiscreteRotation.R90).ShouldBe(RectSide.Right);
            RectSide.Down.RotateSideCounterClockwise( DiscreteRotation.R180).ShouldBe(RectSide.Up);
            RectSide.Down.RotateSideCounterClockwise( DiscreteRotation.R270).ShouldBe(RectSide.Left);
            RectSide.Left.RotateSideCounterClockwise( DiscreteRotation.R0).ShouldBe(RectSide.Left);
            RectSide.Left.RotateSideCounterClockwise( DiscreteRotation.R90).ShouldBe(RectSide.Down);
            RectSide.Left.RotateSideCounterClockwise( DiscreteRotation.R180).ShouldBe(RectSide.Right);
            RectSide.Left.RotateSideCounterClockwise( DiscreteRotation.R270).ShouldBe(RectSide.Up);
            RectSide.Up.RotateSideCounterClockwise( DiscreteRotation.R0).ShouldBe(RectSide.Up);
            RectSide.Up.RotateSideCounterClockwise( DiscreteRotation.R90).ShouldBe(RectSide.Left);
            RectSide.Up.RotateSideCounterClockwise( DiscreteRotation.R180).ShouldBe(RectSide.Down);
            RectSide.Up.RotateSideCounterClockwise( DiscreteRotation.R270).ShouldBe(RectSide.Right);
        }
       
    }
}