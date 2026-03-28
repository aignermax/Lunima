using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis;

/// <summary>
/// Unit tests for <see cref="WaveguideOverlapDetector"/>.
/// </summary>
public class WaveguideOverlapDetectorTests
{
    private readonly WaveguideOverlapDetector _detector = new();

    // ── Basic path overlap ────────────────────────────────────────────────

    [Fact]
    public void DetectOverlaps_NoConnections_ReturnsEmpty()
    {
        var result = _detector.DetectOverlaps(
            Array.Empty<WaveguideConnection>(),
            Array.Empty<ComponentGroup>());

        result.ShouldBeEmpty();
    }

    [Fact]
    public void DetectOverlaps_NullConnections_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            _detector.DetectOverlaps(null!, Array.Empty<ComponentGroup>()));
    }

    [Fact]
    public void DetectOverlaps_NullGroups_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            _detector.DetectOverlaps(Array.Empty<WaveguideConnection>(), null!));
    }

    // ── Straight-segment pair detection ──────────────────────────────────

    [Fact]
    public void DetectOverlaps_CrossingConnections_ReturnsOneIssue()
    {
        // Horizontal segment from (0,50) to (100,50)
        var conn1 = CreateConnectionWithSegment(0, 50, 100, 50);
        // Vertical segment from (50,0) to (50,100) — crosses at (50,50)
        var conn2 = CreateConnectionWithSegment(50, 0, 50, 100);

        var result = _detector.DetectOverlaps(
            new[] { conn1, conn2 },
            Array.Empty<ComponentGroup>());

        result.Count.ShouldBe(1);
        result[0].Type.ShouldBe(DesignIssueType.OverlappingPaths);
    }

    [Fact]
    public void DetectOverlaps_CrossingConnections_ReportsCrossPoint()
    {
        var conn1 = CreateConnectionWithSegment(0, 50, 100, 50);
        var conn2 = CreateConnectionWithSegment(50, 0, 50, 100);

        var result = _detector.DetectOverlaps(
            new[] { conn1, conn2 },
            Array.Empty<ComponentGroup>());

        result[0].X.ShouldBe(50, 0.5);
        result[0].Y.ShouldBe(50, 0.5);
    }

    [Fact]
    public void DetectOverlaps_ParallelConnections_ReturnsEmpty()
    {
        // Two horizontal segments that don't cross
        var conn1 = CreateConnectionWithSegment(0, 0, 100, 0);
        var conn2 = CreateConnectionWithSegment(0, 10, 100, 10);

        var result = _detector.DetectOverlaps(
            new[] { conn1, conn2 },
            Array.Empty<ComponentGroup>());

        result.ShouldBeEmpty();
    }

    [Fact]
    public void DetectOverlaps_NonCrossingPerpendicularSegments_ReturnsEmpty()
    {
        // Horizontal segment on y=0 from x=0 to x=40
        var conn1 = CreateConnectionWithSegment(0, 0, 40, 0);
        // Vertical segment on x=60, does not reach y=0
        var conn2 = CreateConnectionWithSegment(60, 10, 60, 100);

        var result = _detector.DetectOverlaps(
            new[] { conn1, conn2 },
            Array.Empty<ComponentGroup>());

        result.ShouldBeEmpty();
    }

    [Fact]
    public void DetectOverlaps_ConnectionWithNoPath_Ignored()
    {
        var conn1 = CreateConnectionWithSegment(0, 0, 100, 100);
        var conn2 = CreateConnectionWithoutPath();

        var result = _detector.DetectOverlaps(
            new[] { conn1, conn2 },
            Array.Empty<ComponentGroup>());

        result.ShouldBeEmpty();
    }

    // ── Connection vs frozen path ─────────────────────────────────────────

    [Fact]
    public void DetectOverlaps_ConnectionCrossesFrozenPath_ReturnsIssue()
    {
        // Horizontal connection from (0,50) to (100,50)
        var conn = CreateConnectionWithSegment(0, 50, 100, 50);

        // Group with frozen vertical path crossing at (50,50)
        var group = CreateGroupWithFrozenPath(50, 0, 50, 100);

        var result = _detector.DetectOverlaps(new[] { conn }, new[] { group });

        result.Count.ShouldBe(1);
        result[0].Type.ShouldBe(DesignIssueType.OverlappingPaths);
    }

    [Fact]
    public void DetectOverlaps_ConnectionDoesNotCrossFrozenPath_ReturnsEmpty()
    {
        // Horizontal connection on y=0
        var conn = CreateConnectionWithSegment(0, 0, 100, 0);

        // Vertical frozen path far away on x=200
        var group = CreateGroupWithFrozenPath(200, 0, 200, 100);

        var result = _detector.DetectOverlaps(new[] { conn }, new[] { group });

        result.ShouldBeEmpty();
    }

    [Fact]
    public void DetectOverlaps_ConnectionCrossesFrozenPath_DescriptionContainsPathLabels()
    {
        var conn = CreateConnectionWithSegment(0, 50, 100, 50);
        var group = CreateGroupWithFrozenPath(50, 0, 50, 100);
        group.Identifier = "TestGroup";

        var result = _detector.DetectOverlaps(new[] { conn }, new[] { group });

        result[0].Description.ShouldContain("TestGroup");
        result[0].Description.ShouldContain("↔");
    }

    [Fact]
    public void DetectOverlaps_ConnectionCrossesFrozenPath_ConnectionIsAvailable()
    {
        var conn = CreateConnectionWithSegment(0, 50, 100, 50);
        var group = CreateGroupWithFrozenPath(50, 0, 50, 100);

        var result = _detector.DetectOverlaps(new[] { conn }, new[] { group });

        result[0].Connection.ShouldBe(conn);
    }

    // ── Two frozen paths in different groups ──────────────────────────────

    [Fact]
    public void DetectOverlaps_TwoFrozenPathsCross_ReturnsIssue()
    {
        var group1 = CreateGroupWithFrozenPath(0, 50, 100, 50);  // horizontal
        var group2 = CreateGroupWithFrozenPath(50, 0, 50, 100);  // vertical

        var result = _detector.DetectOverlaps(
            Array.Empty<WaveguideConnection>(),
            new[] { group1, group2 });

        result.Count.ShouldBe(1);
        result[0].Type.ShouldBe(DesignIssueType.OverlappingPaths);
    }

    [Fact]
    public void DetectOverlaps_TwoFrozenPathsCross_ConnectionIsNull()
    {
        var group1 = CreateGroupWithFrozenPath(0, 50, 100, 50);
        var group2 = CreateGroupWithFrozenPath(50, 0, 50, 100);

        var result = _detector.DetectOverlaps(
            Array.Empty<WaveguideConnection>(),
            new[] { group1, group2 });

        // Both paths are frozen so no WaveguideConnection is involved
        result[0].Connection.ShouldBeNull();
    }

    // ── Multiple overlaps ─────────────────────────────────────────────────

    [Fact]
    public void DetectOverlaps_MultipleOverlaps_ReturnsAllIssues()
    {
        var conn1 = CreateConnectionWithSegment(0, 50, 100, 50);
        var conn2 = CreateConnectionWithSegment(0, 70, 100, 70);
        var conn3 = CreateConnectionWithSegment(50, 0, 50, 100);  // crosses conn1 and conn2

        var result = _detector.DetectOverlaps(
            new[] { conn1, conn2, conn3 },
            Array.Empty<ComponentGroup>());

        result.Count.ShouldBe(2);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static WaveguideConnection CreateConnectionWithSegment(
        double x1, double y1, double x2, double y2)
    {
        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        comp1.PhysicalX = x1;
        comp1.PhysicalY = y1;
        var pin1 = new PhysicalPin
        {
            Name = "out",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            ParentComponent = comp1
        };
        comp1.PhysicalPins.Add(pin1);

        var comp2 = TestComponentFactory.CreateStraightWaveGuide();
        comp2.PhysicalX = x2;
        comp2.PhysicalY = y2;
        var pin2 = new PhysicalPin
        {
            Name = "in",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            ParentComponent = comp2
        };
        comp2.PhysicalPins.Add(pin2);

        var conn = new WaveguideConnection { StartPin = pin1, EndPin = pin2 };
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y1, x2, y2, 0));
        conn.RestoreCachedPath(path);
        return conn;
    }

    private static WaveguideConnection CreateConnectionWithoutPath()
    {
        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        var pin1 = new PhysicalPin
        {
            Name = "out",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            ParentComponent = comp1
        };
        comp1.PhysicalPins.Add(pin1);

        var comp2 = TestComponentFactory.CreateStraightWaveGuide();
        var pin2 = new PhysicalPin
        {
            Name = "in",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            ParentComponent = comp2
        };
        comp2.PhysicalPins.Add(pin2);

        return new WaveguideConnection { StartPin = pin1, EndPin = pin2 };
    }

    private static ComponentGroup CreateGroupWithFrozenPath(
        double x1, double y1, double x2, double y2)
    {
        var group = new ComponentGroup("TestGroup");
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y1, x2, y2, 0));

        var pin1 = CreateGroupPin(group, "p1");
        var pin2 = CreateGroupPin(group, "p2");

        group.InternalPaths.Add(new FrozenWaveguidePath
        {
            Path = path,
            StartPin = pin1,
            EndPin = pin2
        });
        return group;
    }

    private static PhysicalPin CreateGroupPin(ComponentGroup group, string name)
    {
        return new PhysicalPin
        {
            Name = name,
            ParentComponent = group,
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0
        };
    }
}
