using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using Shouldly;
using Xunit;
using Component = CAP_Core.Components.Core.Component;

namespace UnitTests.Analysis;

/// <summary>
/// Tests for <see cref="ComponentFootprintOverlapChecker"/> and its wiring into
/// <see cref="DesignValidator"/>: top-level placed items (components and groups,
/// one rotation-aware footprint each) must be flagged when they physically overlap,
/// while touching edges and group-internal layouts stay silent.
/// </summary>
public class ComponentFootprintOverlapCheckerTests
{
    private readonly ComponentFootprintOverlapChecker _checker = new();

    [Fact]
    public void TwoOverlappingComponents_ReportOneIssueNamingBothAndTheOverlapSize()
    {
        var first = CreateComponent("AND0", x: 0, y: 0, width: 100, height: 50);
        var second = CreateComponent("NOT0", x: 80, y: 20, width: 100, height: 50);

        var issues = _checker.DetectOverlaps(new[] { first, second });

        issues.Count.ShouldBe(1);
        issues[0].Type.ShouldBe(DesignIssueType.ComponentFootprintOverlap);
        issues[0].Description.ShouldContain("AND0");
        issues[0].Description.ShouldContain("NOT0");
        // Overlap rectangle: x 80..100 (20 µm), y 20..50 (30 µm).
        issues[0].Description.ShouldContain("20.0 × 30.0 µm");
        issues[0].X.ShouldBe(90, 0.001);
        issues[0].Y.ShouldBe(35, 0.001);
    }

    [Fact]
    public void TouchingEdges_ReportNoIssue()
    {
        var first = CreateComponent("A", x: 0, y: 0, width: 100, height: 50);
        var second = CreateComponent("B", x: 100, y: 0, width: 100, height: 50);

        _checker.DetectOverlaps(new[] { first, second }).ShouldBeEmpty();
    }

    [Fact]
    public void OverlapWithinTolerance_ReportNoIssue()
    {
        var first = CreateComponent("A", x: 0, y: 0, width: 100, height: 50);
        var second = CreateComponent("B",
            x: 100 - ComponentFootprintOverlapChecker.OverlapToleranceMicrometers / 2,
            y: 0, width: 100, height: 50);

        _checker.DetectOverlaps(new[] { first, second }).ShouldBeEmpty();
    }

    [Fact]
    public void DisjointComponents_ReportNoIssue()
    {
        var first = CreateComponent("A", x: 0, y: 0, width: 100, height: 50);
        var second = CreateComponent("B", x: 500, y: 500, width: 100, height: 50);

        _checker.DetectOverlaps(new[] { first, second }).ShouldBeEmpty();
    }

    [Fact]
    public void Rotated90Component_OverlappingViaSwappedFootprint_ReportsOneIssue()
    {
        var rotated = CreateComponent("Rotated", x: 0, y: 0, width: 100, height: 40);
        ComponentPoseTransform.Rotate90CounterClockwise(rotated);
        rotated.WidthMicrometers.ShouldBe(40);
        rotated.HeightMicrometers.ShouldBe(100);

        // Overlaps the rotated 40 × 100 footprint (x 30..40, y 50..100) but would
        // stand clear of the unrotated 100 × 40 one (its y range ends at 40).
        var neighbour = CreateComponent("Neighbour", x: 30, y: 50, width: 100, height: 50);

        var issues = _checker.DetectOverlaps(new[] { rotated, neighbour });

        issues.Count.ShouldBe(1);
        issues[0].Description.ShouldContain("Rotated");
        issues[0].Description.ShouldContain("Neighbour");
    }

    [Fact]
    public void ChildrenInsideSameGroup_AreNeverFlaggedAgainstEachOther()
    {
        var group = new ComponentGroup("Gate");
        group.AddChild(CreateComponent("ChildA", x: 0, y: 0, width: 100, height: 50));
        group.AddChild(CreateComponent("ChildB", x: 10, y: 10, width: 100, height: 50));

        _checker.DetectOverlaps(new[] { group }).ShouldBeEmpty(
            "a group's internal layout is the group's business — only the group footprint counts");
    }

    [Fact]
    public void GroupChildrenPassedDirectly_AreSkippedBecauseTheyAreNotTopLevel()
    {
        var group = new ComponentGroup("Gate");
        var childA = CreateComponent("ChildA", x: 0, y: 0, width: 100, height: 50);
        var childB = CreateComponent("ChildB", x: 10, y: 10, width: 100, height: 50);
        group.AddChild(childA);
        group.AddChild(childB);

        _checker.DetectOverlaps(new Component[] { group, childA, childB }).ShouldBeEmpty();
    }

    [Fact]
    public void GroupOverlappingLooseComponent_ReportsOneIssueNamingBoth()
    {
        var group = new ComponentGroup("AND0");
        group.AddChild(CreateComponent("ChildA", x: 0, y: 0, width: 100, height: 50));
        group.AddChild(CreateComponent("ChildB", x: 0, y: 60, width: 100, height: 50));
        var loose = CreateComponent("NOT0", x: 50, y: 40, width: 100, height: 50);

        var issues = _checker.DetectOverlaps(new Component[] { group, loose });

        issues.Count.ShouldBe(1);
        issues[0].Type.ShouldBe(DesignIssueType.ComponentFootprintOverlap);
        issues[0].Description.ShouldContain("AND0");
        issues[0].Description.ShouldContain("NOT0");
    }

    [Fact]
    public void GroupStandingClearOfLooseComponent_ReportsNoIssue()
    {
        var group = new ComponentGroup("AND0");
        group.AddChild(CreateComponent("ChildA", x: 0, y: 0, width: 100, height: 50));
        var loose = CreateComponent("NOT0", x: 0, y: 200, width: 100, height: 50);

        _checker.DetectOverlaps(new Component[] { group, loose }).ShouldBeEmpty();
    }

    [Fact]
    public void VirtualAnalysisTool_IsNeverFlagged()
    {
        var analyzer = CreateComponent("Analyzer", x: 0, y: 0, width: 100, height: 50);
        analyzer.NazcaFunctionName = Component.AnalysisToolNazcaSentinel;
        var circuit = CreateComponent("Circuit", x: 10, y: 10, width: 100, height: 50);

        _checker.DetectOverlaps(new[] { analyzer, circuit }).ShouldBeEmpty(
            "virtual analysis tools have no fabrication geometry");
    }

    [Fact]
    public void DesignValidator_FullAggregation_ReportsFootprintOverlapOnce()
    {
        var first = CreateComponent("AND0", x: 0, y: 0, width: 100, height: 50);
        var second = CreateComponent("NOT0", x: 80, y: 20, width: 100, height: 50);

        var issues = new DesignValidator().Validate(
            Array.Empty<WaveguideConnection>(),
            Array.Empty<ComponentGroup>(),
            new Component[] { first, second });

        issues.Count(i => i.Type == DesignIssueType.ComponentFootprintOverlap).ShouldBe(1);
    }

    [Fact]
    public void DesignValidator_ComponentsOnlyOverload_ReportsFootprintOverlap()
    {
        var first = CreateComponent("AND0", x: 0, y: 0, width: 100, height: 50);
        var second = CreateComponent("NOT0", x: 80, y: 20, width: 100, height: 50);

        var issues = new DesignValidator().Validate(
            Array.Empty<WaveguideConnection>(),
            new Component[] { first, second });

        issues.Count(i => i.Type == DesignIssueType.ComponentFootprintOverlap).ShouldBe(1);
    }

    private static Component CreateComponent(
        string identifier, double x, double y, double width, double height)
    {
        var component = TestComponentFactory.CreateStraightWaveGuide();
        component.Identifier = identifier;
        component.PhysicalX = x;
        component.PhysicalY = y;
        component.WidthMicrometers = width;
        component.HeightMicrometers = height;
        return component;
    }
}
