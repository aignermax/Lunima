using CAP_Core.Components.Connections;
using CAP_Core.Components.Connections.Ownership;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;
using Xunit;
using static UnitTests.Dies.DieTestFixtures;

namespace UnitTests.Connections.Ownership;

/// <summary>
/// Storing a wire in its owning group keeps its geometry and settings, re-expresses its pins
/// inside the owner and drops the boundary pins it consumes; undo restores them in place.
/// Load-time migration moves every misplaced wire to its owner and is idempotent.
/// </summary>
public class GroupWireHomingTests
{
    [Fact]
    public void Home_StoresTheWireInM_DropsTheConsumedBoundaryPins_UnhomeRestoresThemInPlace()
    {
        var f = new OwnershipFixtures();
        var pinsBefore = f.M.ExternalPins.ToList();
        var wire = RoutedWire(f.MPinOfP1East, f.MPinOfP2West);
        wire.WidthMicrometers = 0.8;

        var frozen = GroupWireHoming.Freeze(wire, f.M);
        var home = GroupWireHoming.Home(f.M, frozen);

        f.M.InternalPaths.ShouldContain(frozen);
        frozen.WidthMicrometers.ShouldBe(0.8, "the wire's settings travel with it");
        frozen.Path.Segments.Count.ShouldBe(1);
        frozen.StartPin!.ParentComponent.ShouldBe(f.K1);
        frozen.EndPin!.ParentComponent.ShouldBe(f.K2);
        f.M.ExternalPins.Count.ShouldBe(pinsBefore.Count - 2, "the wired pins are inside M now");

        GroupWireHoming.Unhome(home);

        f.M.InternalPaths.ShouldBeEmpty();
        f.M.ExternalPins.ShouldBe(pinsBefore);
    }

    [Fact]
    public void Migration_MovesARootWireBetweenK1AndK2IntoM_AndIsIdempotent()
    {
        var f = new OwnershipFixtures();
        var wire = RoutedWire(f.MPinOfP1East, f.MPinOfP2West);

        var plan = WireHomeMigration.Plan(new Component[] { f.M }, new[] { wire });

        plan.RootWiresToGroups.ShouldHaveSingleItem().Owner.ShouldBe(f.M);
        GroupWireHoming.Home(f.M, GroupWireHoming.Freeze(wire, f.M));
        WireHomeMigration.Plan(new Component[] { f.M }, Array.Empty<WaveguideConnection>())
            .IsEmpty.ShouldBeTrue("once every wire sits with its owner nothing moves");
    }

    [Fact]
    public void Migration_MovesAFrozenPathStoredTooHighDownIntoItsOwner()
    {
        var f = new OwnershipFixtures();
        var outer = OwnershipFixtures.GroupExposingAll("Outer", f.M);
        var tooHigh = new FrozenWaveguidePath
        {
            StartPin = f.P1.PhysicalPins[0],
            EndPin = f.P1.PhysicalPins[1],
            Path = new RoutedPath(),
        };
        outer.AddInternalPath(tooHigh);

        var plan = WireHomeMigration.Plan(new Component[] { outer }, Array.Empty<WaveguideConnection>());
        WireHomeMigration.ApplyModelMoves(plan);

        outer.InternalPaths.ShouldBeEmpty();
        f.K1.InternalPaths.ShouldContain(tooHigh);
        WireHomeMigration.Plan(new Component[] { outer }, Array.Empty<WaveguideConnection>()).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Migration_BringsWiresStoredInADieOntoTheAssembly_LeavesFacetLinksAndOrdinaryRootWires()
    {
        var a = Part("a", 100, 100);
        var b = Part("b", 400, 100);
        var die = Die("Die 1", 0, 0, 1000, 500, null, a, b);
        var stored = new FrozenWaveguidePath { StartPin = a.PhysicalPins[1], EndPin = b.PhysicalPins[0], Path = new RoutedPath() };
        die.AddInternalPath(stored);
        var x = Part("x", 2000, 0);
        var y = Part("y", 2300, 0);
        var rootWire = RoutedWire(x.PhysicalPins[1], y.PhysicalPins[0]);

        var plan = WireHomeMigration.Plan(new Component[] { die, x, y }, new[] { rootWire });
        WireHomeMigration.ApplyModelMoves(plan);

        plan.DieWiresToSurface.ShouldHaveSingleItem().Path.ShouldBe(stored);
        plan.RootWiresToGroups.ShouldBeEmpty("a wire between two top-level parts stays at the root");
        die.InternalPaths.ShouldBeEmpty();
    }

    [Fact]
    public void PlainSingleDieDesign_WithWiresBetweenGroups_IsUnchanged()
    {
        var f1 = new OwnershipFixtures(0, 0);
        var f2 = new OwnershipFixtures(0, 1000);
        var wire = RoutedWire(f1.MPinOfP2West, f2.MPinOfP1East);

        WireHomeMigration.Plan(new Component[] { f1.M, f2.M }, new[] { wire }).IsEmpty.ShouldBeTrue();
    }

    private static WaveguideConnection RoutedWire(PhysicalPin start, PhysicalPin end)
    {
        var connection = new WaveguideConnection { StartPin = start, EndPin = end };
        var (x1, y1) = start.GetAbsolutePosition();
        var (x2, y2) = end.GetAbsolutePosition();
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y1, x2, y2, 0));
        connection.RestoreCachedPath(path);
        return connection;
    }
}
