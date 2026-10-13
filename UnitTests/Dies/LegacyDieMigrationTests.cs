using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using Shouldly;
using Xunit;
using static UnitTests.Dies.DieTestFixtures;

namespace UnitTests.Dies;

/// <summary>
/// Old files: only genuine multi-chiplet designs become die assemblies; everything else
/// stays a single-die design exactly as before.
/// </summary>
public class LegacyDieMigrationTests
{
    [Fact]
    public void LinkedChiplets_BecomeDies_FacetSidesStayOnTheEdge()
    {
        var (a, b, link) = TwoLinkedChiplets();

        var migrated = LegacyDieMigration.Migrate(new Component[] { a, b }, new[] { link });

        migrated.ShouldBe(new[] { a, b }, ignoreOrder: true);
        var outlineA = DieAssembly.OutlineOf(a);
        outlineA.Right.ShouldBe(300, 1e-9, "chiplet A's facet faces east and stays on the edge");
        outlineA.X.ShouldBe(-LegacyDieMigration.ContentMarginMicrometers, 1e-9, "the side without a facet gets a margin");
        DieAssembly.OutlineOf(b).X.ShouldBe(300, 1e-9);
        a.PhysicalX.ShouldBe(outlineA.X, 1e-9, "the die origin is its outline's top-left");
    }

    [Fact]
    public void GroupsOfDifferentProcesses_BecomeDies()
    {
        var sin = new ComponentGroup("SiN") { ProcessBinding = Process("SiN 300", "SiN", "sin-pdk") };
        sin.AddChild(Part("s", 0, 0));
        var inp = new ComponentGroup("InP") { ProcessBinding = Process("InP gen", "InP", "inp-pdk") };
        inp.AddChild(Part("i", 1000, 0));

        LegacyDieMigration.Migrate(new Component[] { sin, inp }, Array.Empty<WaveguideConnection>())
            .Count.ShouldBe(2);
        sin.IsDie.ShouldBeTrue();
    }

    [Fact]
    public void OrdinaryGroupsOfOneProcess_StaySingleDie()
    {
        var process = Process("SiN 300", "SiN", "sin-pdk");
        var g1 = new ComponentGroup("cell 1") { ProcessBinding = process };
        g1.AddChild(Part("a", 0, 0));
        var g2 = new ComponentGroup("cell 2") { ProcessBinding = process };
        g2.AddChild(Part("b", 500, 0));

        LegacyDieMigration.Migrate(new Component[] { g1, g2, Part("loose", 900, 0) }, Array.Empty<WaveguideConnection>())
            .ShouldBeEmpty();
        g1.IsDie.ShouldBeFalse();
    }

    [Fact]
    public void DesignWithDies_IsNotMigratedAgain()
    {
        var (a, b, link) = TwoLinkedChiplets();
        a.Die = new DieDefinition(5000, 5000);

        LegacyDieMigration.Migrate(new Component[] { a, b }, new[] { link }).ShouldBeEmpty();
        b.IsDie.ShouldBeFalse();
    }

    /// <summary>Two chiplets butt-coupled at x = 300 through their edge-coupler facets.</summary>
    internal static (ComponentGroup A, ComponentGroup B, WaveguideConnection Link) TwoLinkedChiplets()
    {
        var couplerA = EdgeCoupler("a_ec", 200, 0);
        couplerA.PhysicalPins[0].AngleDegrees = 0;
        couplerA.PhysicalPins[0].OffsetXMicrometers = CouplerWidth;
        couplerA.PhysicalPins[1].AngleDegrees = 180;
        couplerA.PhysicalPins[1].OffsetXMicrometers = 0;
        var a = new ComponentGroup("Chiplet A");
        a.AddChild(Part("a_wg", 0, 0, 150, CouplerHeight));
        a.AddChild(couplerA);

        var couplerB = EdgeCoupler("b_ec", 300, 0);
        var b = new ComponentGroup("Chiplet B") { PhysicalX = 300 };
        b.AddChild(couplerB);
        b.AddChild(Part("b_wg", 450, 0, 150, CouplerHeight));

        var link = new WaveguideConnection { StartPin = couplerA.PhysicalPins[0], EndPin = couplerB.PhysicalPins[0] };
        return (a, b, link);
    }
}
