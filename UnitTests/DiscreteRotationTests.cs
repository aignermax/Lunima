using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Helpers;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests
{
    public class DiscreteRotationTests
    {
        [Fact]
        public void CalculateCyclesTillTargetTests()
        {

            DiscreteRotation rotation90 = DiscreteRotation.R90;
            DiscreteRotation rotation180 = DiscreteRotation.R180;

            int Cycles0 = rotation90.CalculateCyclesTillTargetRotation(DiscreteRotation.R90);
            int Cycles1 = rotation90.CalculateCyclesTillTargetRotation( DiscreteRotation.R180);
            int Cycles2 = rotation90.CalculateCyclesTillTargetRotation( DiscreteRotation.R270);
            int Cycles3 = DiscreteRotationExtensions.CalculateCyclesTillTargetRotation(DiscreteRotation.R0, DiscreteRotation.R270);
            int Cycles4 = DiscreteRotationExtensions.CalculateCyclesTillTargetRotation(DiscreteRotation.R270, DiscreteRotation.R0);
            Cycles0.ShouldBe(0);
            Cycles1.ShouldBe(1);
            Cycles2.ShouldBe(2);
            Cycles3.ShouldBe(3);
            Cycles4.ShouldBe(1);
        }
        [Fact]
        public void Rotation90SideCorrelationTests()
        {
            ((int)DiscreteRotation.R0).ShouldBe((int)RectSide.Right);
            ((int)DiscreteRotation.R90).ShouldBe((int)RectSide.Down, "Because both the rotation and Orientation should be clockwise as it is in the Godot Engine to be easily compatible");
            ((int)DiscreteRotation.R180).ShouldBe((int)RectSide.Left);
            ((int)DiscreteRotation.R270).ShouldBe((int)RectSide.Up);
        }
       
    }
}