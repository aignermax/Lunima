using CAP_Core.Components;
using CAP_Core.Components.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Routing.Analysis;
using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis;

public class RoutingComplexityAnalyzerTests
{
    private readonly RoutingComplexityAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_NoConnections_ReturnsZeroMetrics()
    {
        var connections = new List<WaveguideConnection>();

        var result = _analyzer.Analyze(connections);

        result.TotalBendCount.ShouldBe(0);
        result.AverageBendsPerConnection.ShouldBe(0);
        result.CongestionScore.ShouldBe(0);
        result.LongestPathMicrometers.ShouldBe(0);
        result.ConnectionCount.ShouldBe(0);
        result.HasWarnings.ShouldBeFalse();
        result.HasErrors.ShouldBeFalse();
    }

    [Fact]
    public void Analyze_SingleStraightConnection_CorrectMetrics()
    {
        var connection = CreateConnectionWithPath(
            lengthMicrometers: 100, bendCount: 0);
        var connections = new List<WaveguideConnection> { connection };

        var result = _analyzer.Analyze(connections);

        result.TotalBendCount.ShouldBe(0);
        result.AverageBendsPerConnection.ShouldBe(0);
        result.LongestPathMicrometers.ShouldBe(100, 1.0);
        result.ConnectionCount.ShouldBe(1);
    }

    [Fact]
    public void Analyze_MultiplePaths_SumsBendsCorrectly()
    {
        var conn1 = CreateConnectionWithPath(100, bendCount: 2);
        var conn2 = CreateConnectionWithPath(200, bendCount: 3);
        var connections = new List<WaveguideConnection> { conn1, conn2 };

        var result = _analyzer.Analyze(connections);

        result.TotalBendCount.ShouldBe(5, 0.1);
        result.AverageBendsPerConnection.ShouldBe(2.5, 0.1);
        result.ConnectionCount.ShouldBe(2);
    }

    [Fact]
    public void Analyze_MultiplePaths_FindsLongestPath()
    {
        var conn1 = CreateConnectionWithPath(100, bendCount: 0);
        var conn2 = CreateConnectionWithPath(500, bendCount: 0);
        var conn3 = CreateConnectionWithPath(200, bendCount: 0);
        var connections = new List<WaveguideConnection> { conn1, conn2, conn3 };

        var result = _analyzer.Analyze(connections);

        result.LongestPathMicrometers.ShouldBe(500, 1.0);
    }

    [Fact]
    public void Analyze_WithGrid_CalculatesCongestion()
    {
        // Use cellSize=1 so waveguide marking works at fine resolution
        var grid = new PathfindingGrid(0, 0, 20, 20, cellSize: 1);
        // Grid is 20x20 = 400 cells, all free initially
        // Mark a waveguide path across the grid (width=2 ensures cells are hit)
        var segments = new List<PathSegment>
        {
            new StraightSegment(0, 10, 20, 10, 0)
        };
        grid.AddWaveguideObstacle(Guid.NewGuid(), segments, 2.0);

        var connections = new List<WaveguideConnection>
        {
            CreateConnectionWithPath(100, bendCount: 0)
        };

        var result = _analyzer.Analyze(connections, grid);

        result.CongestionScore.ShouldBeGreaterThan(0);
        result.CongestionScore.ShouldBeLessThanOrEqualTo(1.0);
    }

    [Fact]
    public void Analyze_NullGrid_CongestionIsZero()
    {
        var connections = new List<WaveguideConnection>
        {
            CreateConnectionWithPath(100, bendCount: 1)
        };

        var result = _analyzer.Analyze(connections, grid: null);

        result.CongestionScore.ShouldBe(0);
    }

    [Fact]
    public void Analyze_HighBendCount_EmitsWarning()
    {
        var thresholds = new RoutingComplexityThresholds
        {
            MaxTotalBendCount = 5.0
        };
        var analyzer = new RoutingComplexityAnalyzer(thresholds);

        var connections = new List<WaveguideConnection>
        {
            CreateConnectionWithPath(100, bendCount: 3),
            CreateConnectionWithPath(200, bendCount: 4)
        };

        var result = analyzer.Analyze(connections);

        result.HasWarnings.ShouldBeTrue();
        result.Warnings.ShouldContain(
            d => d.Message.Contains("Total bend count"));
    }

    [Fact]
    public void Analyze_HighAverageBends_EmitsWarning()
    {
        var thresholds = new RoutingComplexityThresholds
        {
            MaxAverageBendsPerConnection = 2.0,
            MaxTotalBendCount = 100.0
        };
        var analyzer = new RoutingComplexityAnalyzer(thresholds);

        var connections = new List<WaveguideConnection>
        {
            CreateConnectionWithPath(100, bendCount: 3),
            CreateConnectionWithPath(200, bendCount: 4)
        };

        var result = analyzer.Analyze(connections);

        result.HasWarnings.ShouldBeTrue();
        result.Warnings.ShouldContain(
            d => d.Message.Contains("Average bends per connection"));
    }

    [Fact]
    public void Analyze_LongPath_EmitsWarning()
    {
        var thresholds = new RoutingComplexityThresholds
        {
            MaxPathLengthMicrometers = 1000.0
        };
        var analyzer = new RoutingComplexityAnalyzer(thresholds);

        var connections = new List<WaveguideConnection>
        {
            CreateConnectionWithPath(2000, bendCount: 0)
        };

        var result = analyzer.Analyze(connections);

        result.HasWarnings.ShouldBeTrue();
        result.Warnings.ShouldContain(
            d => d.Message.Contains("Longest path"));
    }

    [Fact]
    public void Analyze_BlockedFallback_EmitsError()
    {
        var connection = CreateConnectionWithPath(100, bendCount: 1,
            isBlockedFallback: true);
        var connections = new List<WaveguideConnection> { connection };

        var result = _analyzer.Analyze(connections);

        result.HasErrors.ShouldBeTrue();
        result.BlockedFallbackCount.ShouldBe(1);
        result.Errors.ShouldContain(
            d => d.Message.Contains("fallback paths"));
    }

    [Fact]
    public void Analyze_BelowThresholds_NoDiagnostics()
    {
        var thresholds = new RoutingComplexityThresholds
        {
            MaxTotalBendCount = 100,
            MaxAverageBendsPerConnection = 10,
            MaxCongestionScore = 0.9,
            MaxPathLengthMicrometers = 10000
        };
        var analyzer = new RoutingComplexityAnalyzer(thresholds);

        var connections = new List<WaveguideConnection>
        {
            CreateConnectionWithPath(100, bendCount: 1)
        };

        var result = analyzer.Analyze(connections);

        result.HasWarnings.ShouldBeFalse();
        result.HasErrors.ShouldBeFalse();
        result.Diagnostics.Count.ShouldBe(0);
    }

    [Fact]
    public void Analyze_ConnectionWithNullRoutedPath_SkippedGracefully()
    {
        var component = CreateTestComponent(0, 0);
        var connection = new WaveguideConnection
        {
            StartPin = null!,
            EndPin = null!
        };
        // RoutedPath stays null — no RecalculateTransmission called
        var connections = new List<WaveguideConnection> { connection };

        var result = _analyzer.Analyze(connections);

        result.TotalBendCount.ShouldBe(0);
        result.ConnectionCount.ShouldBe(1);
    }

    [Fact]
    public void GetSummary_ReturnsFormattedString()
    {
        var connections = new List<WaveguideConnection>
        {
            CreateConnectionWithPath(150, bendCount: 2)
        };

        var result = _analyzer.Analyze(connections);

        var summary = result.GetSummary();
        summary.ShouldContain("Connections: 1");
        summary.ShouldContain("Total bends:");
        summary.ShouldContain("Longest path:");
    }

    #region Test Helpers

    private static WaveguideConnection CreateConnectionWithPath(
        double lengthMicrometers,
        int bendCount,
        bool isBlockedFallback = false)
    {
        var component1 = CreateTestComponent(0, 0);
        var component2 = CreateTestComponent(lengthMicrometers, 0);

        var startPin = new PhysicalPin
        {
            Name = "output",
            OffsetXMicrometers = 50,
            OffsetYMicrometers = 25,
            AngleDegrees = 0,
            ParentComponent = component1
        };

        var endPin = new PhysicalPin
        {
            Name = "input",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 25,
            AngleDegrees = 180,
            ParentComponent = component2
        };

        var connection = new WaveguideConnection
        {
            StartPin = startPin,
            EndPin = endPin
        };

        // Build a synthetic RoutedPath via reflection to avoid
        // depending on the full routing engine
        var path = BuildSyntheticPath(
            lengthMicrometers, bendCount, isBlockedFallback);
        SetRoutedPath(connection, path);

        return connection;
    }

    private static RoutedPath BuildSyntheticPath(
        double totalLength, int bendCount, bool isBlockedFallback)
    {
        var path = new RoutedPath { IsBlockedFallback = isBlockedFallback };

        // Add bend segments (each 90 degrees)
        double bendLength = 0;
        for (int i = 0; i < bendCount; i++)
        {
            double radius = 10.0;
            var bend = new BendSegment(0, 0, radius, i * 90.0, 90.0);
            path.Segments.Add(bend);
            bendLength += bend.LengthMicrometers;
        }

        // Remaining length as a straight segment
        double straightLength = Math.Max(0, totalLength - bendLength);
        if (straightLength > 0)
        {
            path.Segments.Add(new StraightSegment(
                0, 0, straightLength, 0, 0));
        }

        return path;
    }

    private static void SetRoutedPath(
        WaveguideConnection connection, RoutedPath path)
    {
        var prop = typeof(WaveguideConnection)
            .GetProperty(nameof(WaveguideConnection.RoutedPath));
        prop!.SetValue(connection, path);
    }

    private static Component CreateTestComponent(double x, double y)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "test",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: $"TestComp_{x}_{y}",
            rotationCounterClock: DiscreteRotation.R0
        );

        component.WidthMicrometers = 50;
        component.HeightMicrometers = 50;
        component.PhysicalX = x;
        component.PhysicalY = y;

        return component;
    }

    #endregion
}
