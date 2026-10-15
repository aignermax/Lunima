using CAP_Core.Components.Connections.Ownership;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;
using static UnitTests.Dies.DieTestFixtures;

namespace UnitTests.Connections.Ownership;

/// <summary>
/// A wire belongs to the lowest common ancestor group of the two parts it joins, whichever
/// level it is drawn on (boundary pins are followed down to the parts they stand for).
/// </summary>
public class ConnectionOwnershipTests
{
    [Fact]
    public void WireBetweenK1AndK2_DrawnOutsideM_BelongsToM()
    {
        var f = new OwnershipFixtures();

        ConnectionOwnership.OwnerOf(f.MPinOfP1East, f.MPinOfP2West).ShouldBe(f.M);
    }

    [Fact]
    public void WireBetweenK1AndK2_DrawnInsideM_BelongsToM()
    {
        var f = new OwnershipFixtures();
        var k1Pin = OwnershipFixtures.BoundaryPin(f.K1, f.P1.PhysicalPins[1]);
        var k2Pin = OwnershipFixtures.BoundaryPin(f.K2, f.P2.PhysicalPins[0]);

        ConnectionOwnership.OwnerOf(k1Pin, k2Pin).ShouldBe(f.M);
    }

    [Fact]
    public void WireBetweenTwoPinsOfOnePart_BelongsToThePartsGroup()
    {
        var f = new OwnershipFixtures();

        ConnectionOwnership.OwnerOf(f.P1.PhysicalPins[0], f.P1.PhysicalPins[1]).ShouldBe(f.K1);
    }

    [Fact]
    public void WireBetweenTopLevelParts_BelongsToTheRoot()
    {
        var a = Part("a", 0, 0);
        var b = Part("b", 200, 0);

        ConnectionOwnership.OwnerOf(a.PhysicalPins[1], b.PhysicalPins[0]).ShouldBeNull();
    }

    [Fact]
    public void WireBetweenAPartOnADieAndAGroupOnTheSameDie_BelongsToTheDie()
    {
        var f = new OwnershipFixtures(200, 200);
        var loose = Part("loose", 100, 600);
        var die = Die("Die 1", 0, 0, 2000, 1000, null, f.M, loose);

        ConnectionOwnership.OwnerOf(loose.PhysicalPins[1], f.MPinOfP1East).ShouldBe(die);
        ConnectionOwnership.DieOf(f.MPinOfP2West).ShouldBe(die);
    }

    [Fact]
    public void IsBelow_TellsWhetherTheOwnerLiesUnderTheLevelTheWireWasDrawnOn()
    {
        var f = new OwnershipFixtures();

        ConnectionOwnership.IsBelow(f.K1, null).ShouldBeTrue();
        ConnectionOwnership.IsBelow(f.K1, f.M).ShouldBeTrue();
        ConnectionOwnership.IsBelow(f.M, f.M).ShouldBeFalse();
        ConnectionOwnership.IsBelow(f.M, f.K1).ShouldBeFalse();
    }

    [Fact]
    public void PinInside_FollowsBoundaryPinsIntoTheOwner()
    {
        var f = new OwnershipFixtures();

        var inside = ConnectionOwnership.PinInside(f.MPinOfP1East, f.M);

        inside.ParentComponent.ShouldBe(f.K1, "M's boundary pin is re-expressed as K1's pin inside M");
        ConnectionOwnership.PinInside(f.P1.PhysicalPins[0], f.M).ShouldBe(f.P1.PhysicalPins[0]);
    }
}
