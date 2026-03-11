using CAP_Core.Analysis;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using Shouldly;

namespace UnitTests.Analysis;

/// <summary>
/// Unit tests for <see cref="DesignValidator"/>.
/// </summary>
public class DesignValidatorTests
{
    private readonly DesignValidator _validator = new();

    [Fact]
    public void Validate_EmptyConnections_ReturnsNoIssues()
    {
        var result = _validator.Validate(Array.Empty<WaveguideConnection>());

        result.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_NullConnections_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => _validator.Validate(null!));
    }

    [Fact]
    public void Validate_ValidConnection_ReturnsNoIssues()
    {
        var connection = CreateTestConnection();
        connection.RestoreCachedPath(CreateValidPath());

        var result = _validator.Validate(new[] { connection });

        result.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_InvalidGeometry_ReturnsIssue()
    {
        var connection = CreateTestConnection();
        var path = CreateValidPath();
        path.IsInvalidGeometry = true;
        connection.RestoreCachedPath(path);

        var result = _validator.Validate(new[] { connection });

        result.Count.ShouldBe(1);
        result[0].Type.ShouldBe(DesignIssueType.InvalidGeometry);
        result[0].Connection.ShouldBe(connection);
    }

    [Fact]
    public void Validate_BlockedPath_ReturnsIssue()
    {
        var connection = CreateTestConnection();
        var path = CreateValidPath();
        path.IsBlockedFallback = true;
        connection.RestoreCachedPath(path);

        var result = _validator.Validate(new[] { connection });

        result.Count.ShouldBe(1);
        result[0].Type.ShouldBe(DesignIssueType.BlockedPath);
    }

    [Fact]
    public void Validate_BothIssues_ReturnsTwoIssues()
    {
        var connection = CreateTestConnection();
        var path = CreateValidPath();
        path.IsInvalidGeometry = true;
        path.IsBlockedFallback = true;
        connection.RestoreCachedPath(path);

        var result = _validator.Validate(new[] { connection });

        result.Count.ShouldBe(2);
        result.ShouldContain(i => i.Type == DesignIssueType.InvalidGeometry);
        result.ShouldContain(i => i.Type == DesignIssueType.BlockedPath);
    }

    [Fact]
    public void Validate_MultipleConnections_FindsAllIssues()
    {
        var conn1 = CreateTestConnection(x1: 0, y1: 0, x2: 100, y2: 0);
        var path1 = CreateValidPath();
        path1.IsInvalidGeometry = true;
        conn1.RestoreCachedPath(path1);

        var conn2 = CreateTestConnection(x1: 200, y1: 0, x2: 300, y2: 0);
        conn2.RestoreCachedPath(CreateValidPath());

        var conn3 = CreateTestConnection(x1: 400, y1: 0, x2: 500, y2: 0);
        var path3 = CreateValidPath();
        path3.IsBlockedFallback = true;
        conn3.RestoreCachedPath(path3);

        var result = _validator.Validate(new[] { conn1, conn2, conn3 });

        result.Count.ShouldBe(2);
    }

    [Fact]
    public void Validate_IssuePosition_IsMidpointOfPins()
    {
        var connection = CreateTestConnection(x1: 100, y1: 200, x2: 300, y2: 400);
        var path = CreateValidPath();
        path.IsInvalidGeometry = true;
        connection.RestoreCachedPath(path);

        var result = _validator.Validate(new[] { connection });

        result[0].X.ShouldBe(200, 0.1);
        result[0].Y.ShouldBe(300, 0.1);
    }

    [Fact]
    public void Validate_IssueDescription_ContainsPinNames()
    {
        var connection = CreateTestConnection();
        var path = CreateValidPath();
        path.IsInvalidGeometry = true;
        connection.RestoreCachedPath(path);

        var result = _validator.Validate(new[] { connection });

        result[0].Description.ShouldContain("pin_a");
        result[0].Description.ShouldContain("pin_b");
    }

    [Fact]
    public void Validate_ConnectionWithNoPath_ReturnsNoIssues()
    {
        var connection = CreateTestConnection();
        // No path assigned — RoutedPath is null

        var result = _validator.Validate(new[] { connection });

        result.ShouldBeEmpty();
    }

    /// <summary>
    /// Creates a test WaveguideConnection with two components at specified positions.
    /// </summary>
    private static WaveguideConnection CreateTestConnection(
        double x1 = 0, double y1 = 0,
        double x2 = 100, double y2 = 0)
    {
        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        comp1.PhysicalX = x1;
        comp1.PhysicalY = y1;
        comp1.PhysicalPins.Add(new PhysicalPin
        {
            Name = "pin_a",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            ParentComponent = comp1
        });

        var comp2 = TestComponentFactory.CreateStraightWaveGuide();
        comp2.PhysicalX = x2;
        comp2.PhysicalY = y2;
        comp2.PhysicalPins.Add(new PhysicalPin
        {
            Name = "pin_b",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            ParentComponent = comp2
        });

        return new WaveguideConnection
        {
            StartPin = comp1.PhysicalPins.Last(),
            EndPin = comp2.PhysicalPins.Last()
        };
    }

    /// <summary>
    /// Creates a valid RoutedPath with a single straight segment.
    /// </summary>
    private static RoutedPath CreateValidPath()
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));
        return path;
    }
}
