using CAP_Core.Analysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;
using static UnitTests.Analysis.Hierarchy.NestedDesignBuilder;
using Component = CAP_Core.Components.Core.Component;

namespace UnitTests.Analysis.Hierarchy;

/// <summary>
/// Geometry design checks on groups nested two levels deep (OUTER ⊃ INNER ⊃ leaves):
/// footprint overlaps, frozen-path overlap / spacing / through-component, frozen-path
/// router flags, min width and chip bounds must all see nested components and paths,
/// in absolute coordinates, without flagging intentional abutment.
/// </summary>
public class NestedGroupGeometryCheckTests
{
    private readonly DesignValidator _validator = new();

    /// <summary>OUTER ⊃ { A, INNER ⊃ { B, C } } with a frozen B.out → C.in path in INNER.</summary>
    private static (ComponentGroup Outer, ComponentGroup Inner, Component B, Component C) BuildNested()
    {
        var b = Leaf("B", 200, 0);
        var c = Leaf("C", 400, 0);
        var inner = Group("INNER", b, c);
        Frozen(inner, Pin(b, "out"), Pin(c, "in"), (300, 10), (400, 10));
        var outer = Group("OUTER", Leaf("A", 0, 0), inner);
        return (outer, inner, b, c);
    }

    private List<DesignIssue> ValidateAll(IEnumerable<WaveguideConnection> connections, params Component[] topLevel) =>
        _validator.Validate(connections, topLevel.OfType<ComponentGroup>(), topLevel, null, 1550);

    [Fact]
    public void NestedChildOverlappingLooseComponent_IsReportedAtTheOverlapWithGroupPath()
    {
        var (outer, _, _, _) = BuildNested();
        var loose = Leaf("L", 420, 5, width: 50);

        var issues = ValidateAll(Array.Empty<WaveguideConnection>(), outer, loose)
            .Where(i => i.Type == DesignIssueType.ComponentFootprintOverlap).ToList();

        issues.Count.ShouldBe(1);
        issues[0].Description.ShouldContain("'OUTER/INNER/C'");
        issues[0].Description.ShouldContain("'L'");
        issues[0].X.ShouldBe(445, 1e-9);
        issues[0].Y.ShouldBe(12.5, 1e-9);
    }

    [Fact]
    public void ChildrenOfTwoDifferentGroups_OverlappingEachOther_AreReported()
    {
        var first = Group("G1", Leaf("P", 0, 0));
        var second = Group("G2", Group("G2IN", Leaf("Q", 50, 5)));

        var issues = new ComponentFootprintOverlapChecker().DetectOverlaps(new Component[] { first, second });

        issues.Count.ShouldBe(1);
        issues[0].Description.ShouldContain("'G1/P'");
        issues[0].Description.ShouldContain("'G2/G2IN/Q'");
    }

    [Fact]
    public void SiblingsAbuttedPinToPin_AreSilent_ButASiblingBuryingAPort_IsReported()
    {
        var checker = new ComponentFootprintOverlapChecker();
        var abutted = Group("INNER", Leaf("B", 0, 0), Leaf("D", 100, 0));
        var buried = Group("INNER", Leaf("B", 0, 0), Leaf("D", 90, 0));

        checker.DetectOverlaps(new Component[] { Group("OUTER", abutted) }).ShouldBeEmpty();
        checker.DetectOverlaps(new Component[] { Group("OUTER", buried) }).Count.ShouldBe(1,
            "D's 'in' port sits 10 µm inside B");
    }

    [Fact]
    public void NestedFrozenPath_CrossingTopLevelConnection_IsAnOverlap()
    {
        var (outer, inner, _, _) = BuildNested();
        Frozen(inner, null, null, (350, -50), (350, 50));
        var connection = Connection(Pin(Leaf("X", 0, -100), "out"), Pin(Leaf("Y", 600, -100), "in"),
            (300, 0), (500, 0));

        var issues = _validator.Validate(new[] { connection }, new[] { outer })
            .Where(i => i.Type == DesignIssueType.OverlappingPaths).ToList();

        issues.ShouldContain(i => i.Description.Contains("Group 'OUTER/INNER' frozen path"));
        issues.ShouldContain(i => i.X == 350 && i.Y == 0);
    }

    [Fact]
    public void NestedFrozenPaths_OfDifferentGroups_CrossingEachOther_AreAnOverlap()
    {
        var (outer, inner, _, _) = BuildNested();
        Frozen(inner, null, null, (350, -50), (350, -10));
        var other = Group("OTHER", Group("DEEP", Leaf("Z", 900, 900)));
        Frozen((ComponentGroup)other.ChildComponents[0], null, null, (300, -30), (400, -30));

        var issues = _validator.Validate(Array.Empty<WaveguideConnection>(), new[] { outer, other });

        issues.ShouldContain(i => i.Type == DesignIssueType.OverlappingPaths
            && i.Description.Contains("OUTER/INNER") && i.Description.Contains("OTHER/DEEP"));
    }

    [Fact]
    public void NestedFrozenPath_TooCloseToTopLevelConnection_IsASpacingViolation()
    {
        var (outer, inner, _, _) = BuildNested();
        Frozen(inner, null, null, (600, 51.5), (700, 51.5));
        var connection = Connection(Pin(Leaf("X", 500, 40), "out"), Pin(Leaf("Y", 800, 40), "in"),
            (600, 50), (700, 50));

        var issues = _validator.Validate(new[] { connection }, new[] { outer }, minWaveguideSpacingMicrometers: 2);

        issues.ShouldContain(i => i.Type == DesignIssueType.WaveguideSpacingViolation
            && i.Description.Contains("OUTER/INNER"));
    }

    [Fact]
    public void NestedFrozenPath_ThroughALooseComponent_IsReported_ButNotAtItsOwnEndpoints()
    {
        var (outer, _, _, _) = BuildNested();
        ValidateAll(Array.Empty<WaveguideConnection>(), outer)
            .ShouldNotContain(i => i.Type == DesignIssueType.WaveguideThroughComponent,
                "the B.out → C.in path ends on its own components' edge pins");

        var blocker = Leaf("M", 340, 0, width: 20);
        var issues = ValidateAll(Array.Empty<WaveguideConnection>(), outer, blocker)
            .Where(i => i.Type == DesignIssueType.WaveguideThroughComponent).ToList();

        issues.Count.ShouldBe(1);
        issues[0].Description.ShouldContain("group 'OUTER/INNER'");
        issues[0].Description.ShouldContain("'M'");
        issues[0].X.ShouldBe(350, 1e-9);
    }

    [Fact]
    public void NestedFrozenPath_WithInvalidGeometryFlag_IsReported()
    {
        var (outer, inner, _, _) = BuildNested();
        inner.InternalPaths[0].Path.IsInvalidGeometry = true;

        var issues = _validator.Validate(Array.Empty<WaveguideConnection>(), new[] { outer });

        var issue = issues.Single(i => i.Type == DesignIssueType.InvalidGeometry);
        issue.Description.ShouldBe("Bend radius violation in group 'OUTER/INNER': B.out to C.in");
        issue.X.ShouldBe(350, 1e-9);
        issue.Y.ShouldBe(10, 1e-9);
    }

    [Fact]
    public void NestedFrozenPath_NarrowerThanTheCrossSectionMinimum_IsReportedWithoutAConnection()
    {
        var (outer, inner, b, c) = BuildNested();
        Pin(b, "out").Layer = 1;
        Pin(c, "in").Layer = 1;
        inner.InternalPaths[0].WidthMicrometers = 0.3;
        var rules = new[] { new WaveguideMinWidthRule(0.5, new[] { 1 }, "xs_test", null) };

        var issues = _validator.Validate(Array.Empty<WaveguideConnection>(), new[] { outer },
            new Component[] { outer }, null, 1550, minWaveguideWidthRules: rules);

        var issue = issues.Single(i => i.Type == DesignIssueType.WaveguideBelowMinWidth);
        issue.Connection.ShouldBeNull("a frozen path has no live connection to highlight");
        issue.Description.ShouldContain("B.out → C.in");
    }

    [Fact]
    public void NestedFrozenPathLeavingTheChip_FlagsTheTopLevelGroupAtTheViolatedEdge()
    {
        var (outer, inner, _, _) = BuildNested();
        Frozen(inner, null, null, (450, 20), (450, -30));

        var issue = _validator.ValidateComponentBounds(new[] { outer }, 1000, 1000).Single();

        issue.Description.ShouldContain("'OUTER'");
        issue.Y.ShouldBe(-30, 1e-9);
    }

    [Fact]
    public void GroupWhoseOriginIsAwayFromItsChildren_InsideTheChip_IsNotOutOfBounds()
    {
        var (outer, _, _, _) = BuildNested();
        outer.PhysicalX = 900;

        _validator.ValidateComponentBounds(new[] { outer }, 1000, 1000).ShouldBeEmpty(
            "only the real geometry counts, not the group origin plus its cached size");
    }
}
