using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using Shouldly;
using Xunit;
using static UnitTests.Dies.DieTestFixtures;

namespace UnitTests.Dies;

/// <summary>Die model: fixed outlines, lookups, origin normalization and placement spots.</summary>
public class DieAssemblyTests
{
    [Fact]
    public void DieBounds_AreTheOutline_NotTheContent()
    {
        var die = Die("Die 1", 100, 200, 2000, 1000, null, Part("p", 300, 400));

        die.WidthMicrometers.ShouldBe(2000);
        die.HeightMicrometers.ShouldBe(1000);
        DieAssembly.OutlineOf(die).ShouldBe(new DieOutline(100, 200, 2000, 1000));
    }

    [Fact]
    public void EmptyDie_KeepsItsOutline()
    {
        var die = Die("Empty", 0, 0, 500, 500);

        die.WidthMicrometers.ShouldBe(500);
        die.HeightMicrometers.ShouldBe(500);
    }

    [Fact]
    public void MovingADie_MovesItsOutline()
    {
        var die = Die("Die 1", 0, 0, 500, 500, null, Part("p", 10, 10));

        die.MoveGroup(1000, 50);

        DieAssembly.OutlineOf(die).ShouldBe(new DieOutline(1000, 50, 500, 500));
        die.ChildComponents[0].PhysicalX.ShouldBe(1010);
    }

    [Fact]
    public void DeepCopy_KeepsTheDieDefinition()
    {
        var die = Die("Die 1", 0, 0, 800, 600, null, Part("p", 10, 10));

        var copy = die.DeepCopy();

        copy.Die.ShouldBe(die.Die);
        copy.WidthMicrometers.ShouldBe(800);
    }

    [Fact]
    public void DieAt_FindsTheDieUnderThePoint()
    {
        var a = Die("A", 0, 0, 1000, 1000);
        var b = Die("B", 2000, 0, 1000, 1000);
        var loose = Part("loose", 5000, 5000);
        var topLevel = new Component[] { a, b, loose };

        DieAssembly.DieAt(topLevel, 2500, 500).ShouldBe(b);
        DieAssembly.DieAt(topLevel, 1500, 500).ShouldBeNull();
        DieAssembly.IsAssembly(topLevel).ShouldBeTrue();
        DieAssembly.IsAssembly(new[] { loose }).ShouldBeFalse();
    }

    [Fact]
    public void DieOf_WalksToTheDie_ThroughNestedGroups()
    {
        var leaf = Part("leaf", 10, 10);
        var nested = new ComponentGroup("cell");
        nested.AddChild(leaf);
        var die = Die("Die 1", 0, 0, 500, 500, null, nested);

        DieAssembly.DieOf(leaf).ShouldBe(die);
        DieAssembly.DieOf(Part("free", 0, 0)).ShouldBeNull();
    }

    [Fact]
    public void NextDieOrigin_GoesRightOfAllDies_TopAligned()
    {
        var outlines = new[] { new DieOutline(0, 100, 1000, 1000), new DieOutline(1500, 0, 500, 500) };

        DieAssembly.NextDieOrigin(outlines, 500).ShouldBe((2500.0, 100.0));
        DieAssembly.NextDieOrigin(Array.Empty<DieOutline>()).ShouldBe((0.0, 0.0));
    }

    [Fact]
    public void TouchingDies_DoNotOverlap_SharedAreaDoes()
    {
        var a = new DieOutline(0, 0, 100, 100);

        a.Overlaps(new DieOutline(100, 0, 100, 100)).ShouldBeFalse();
        a.Overlaps(new DieOutline(99, 0, 100, 100)).ShouldBeTrue();
    }

    [Fact]
    public void NextDieName_SkipsUsedNames()
    {
        DieAssembly.NextDieName(new[] { "Die 1", "die 2" }).ShouldBe("Die 3");
    }

    [Fact]
    public void DieOriginAssign_MovesOriginToOutline_KeepingPinsInPlace()
    {
        var coupler = EdgeCoupler("ec", 50, 60);
        var group = new ComponentGroup("legacy") { PhysicalX = 0, PhysicalY = 0 };
        group.AddChild(coupler);
        group.AddExternalPin(new GroupPin
        {
            Name = "ec_fiber", InternalPin = coupler.PhysicalPins[0], RelativeX = 50, RelativeY = 69.5,
        });

        DieOrigin.Assign(group, new DieDefinition(300, 200, OutlineOffsetXMicrometers: 25, OutlineOffsetYMicrometers: 35));

        group.PhysicalX.ShouldBe(25);
        group.PhysicalY.ShouldBe(35);
        group.Die!.OutlineOffsetXMicrometers.ShouldBe(0);
        group.ExternalPins[0].RelativeX.ShouldBe(25);
        DieAssembly.OutlineOf(group).ShouldBe(new DieOutline(25, 35, 300, 200));
        coupler.PhysicalX.ShouldBe(50, "children keep their absolute positions");
    }

    [Fact]
    public void FindFreeSpot_StaysInsideTheOutline_AndClearOfParts()
    {
        var die = Die("Die 1", 0, 0, 300, 100, null, Part("p", 0, 0, 100, 100));

        var spot = DiePlacement.FindFreeSpot(die, 50, 50, -40, 10);

        spot.ShouldNotBeNull();
        spot!.Value.X.ShouldBeGreaterThanOrEqualTo(100 + DiePlacement.MinGapMicrometers);
        (spot.Value.X + 50).ShouldBeLessThanOrEqualTo(300);
    }

    [Fact]
    public void FindFreeSpot_TooLargePart_HasNoRoom()
    {
        DiePlacement.FindFreeSpot(Die("tiny", 0, 0, 100, 100), 200, 10, 0, 0).ShouldBeNull();
    }

    [Theory]
    [InlineData("SiN", "5 × 5 mm")]
    [InlineData("InP", "4 × 4 mm")]
    [InlineData("Si", "1 × 1 mm")]
    [InlineData(null, "2 × 2 mm")]
    public void SizePresets_FollowTheProcessFamily(string? material, string expectedPreset)
    {
        DieSizePresets.ForMaterial(material).Select(p => p.Name).ShouldContain(expectedPreset);
    }

    [Fact]
    public void InvalidDimensions_AreRejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new DieDefinition(10, 500).Validated());
        Should.Throw<ArgumentOutOfRangeException>(() => new DieDefinition(double.NaN, 500).Validated());
        new DieDefinition(500, 500).Validated().WidthMicrometers.ShouldBe(500);
    }
}
