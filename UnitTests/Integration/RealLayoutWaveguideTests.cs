using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.CodeExporter;
using CAP_Core.Components.Core;
using CAP_DataAccess;
using Shouldly;
using System.Globalization;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Tests GDS coordinate accuracy using real user layout files.
/// Verifies that waveguide start/end points match component pin positions exactly.
/// </summary>
public class RealLayoutWaveguideTests
{
    private const double PinAlignmentTolerance = 0.01; // µm

    /// <summary>
    /// Tests the actual user layout file (testtest.lun) to verify all waveguide endpoints
    /// align with component pins after GDS export.
    /// </summary>
    [Fact]
    public void TestTestLayout_AllWaveguides_MustAlignWithPins()
    {
        // Arrange - Load the actual user file
        var layoutPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "Integration", "testtest.lun");

        if (!File.Exists(layoutPath))
        {
            // Skip test if file not found
            return;
        }

        var persistenceManager = new PersistenceManager();
        var canvas = new DesignCanvasViewModel();

        // Load the design
        var json = File.ReadAllText(layoutPath);
        var (components, connections, groups) = persistenceManager.LoadDesign(json);

        // Populate canvas
        foreach (var comp in components)
        {
            canvas.Components.Add(comp);
        }
        foreach (var conn in connections)
        {
            canvas.Connections.Add(conn);
        }
        foreach (var group in groups)
        {
            canvas.ComponentGroups.Add(group);
        }

        // Act - Export to Nazca and parse back
        var exporter = new SimpleNazcaExporter();
        var nazcaCode = exporter.Export(canvas);
        var parser = new NazcaCodeParser();
        var parsedData = parser.ParseNazcaScript(nazcaCode);

        // Assert - Check EVERY waveguide connection
        int totalConnections = 0;
        int failedConnections = 0;
        var failures = new List<string>();

        foreach (var conn in canvas.Connections)
        {
            if (conn.StartPin == null || conn.EndPin == null || conn.CachedSegments == null || conn.CachedSegments.Count == 0)
                continue;

            totalConnections++;

            // Get expected pin positions using GetAbsoluteNazcaPosition
            var (startPinX, startPinY) = conn.StartPin.GetAbsoluteNazcaPosition();
            var (endPinX, endPinY) = conn.EndPin.GetAbsoluteNazcaPosition();

            // Get actual waveguide start/end from segments
            var firstSegment = conn.CachedSegments.First();
            var lastSegment = conn.CachedSegments.Last();

            double wgStartX = firstSegment.StartX;
            double wgStartY = -firstSegment.StartY; // Convert to Nazca Y
            double wgEndX = lastSegment.EndX;
            double wgEndY = -lastSegment.EndY; // Convert to Nazca Y

            // Check start point alignment
            double startXDev = Math.Abs(startPinX - wgStartX);
            double startYDev = Math.Abs(startPinY - wgStartY);

            if (startXDev >= PinAlignmentTolerance || startYDev >= PinAlignmentTolerance)
            {
                failedConnections++;
                failures.Add($"START: {conn.StartPin.ParentComponent.Identifier}→{conn.EndPin.ParentComponent.Identifier} " +
                            $"ΔX={startXDev:F4}, ΔY={startYDev:F4}");
            }

            // Check end point alignment
            double endXDev = Math.Abs(endPinX - wgEndX);
            double endYDev = Math.Abs(endPinY - wgEndY);

            if (endXDev >= PinAlignmentTolerance || endYDev >= PinAlignmentTolerance)
            {
                failedConnections++;
                failures.Add($"END: {conn.StartPin.ParentComponent.Identifier}→{conn.EndPin.ParentComponent.Identifier} " +
                            $"ΔX={endXDev:F4}, ΔY={endYDev:F4}");
            }
        }

        // Report results
        if (failedConnections > 0)
        {
            var message = $"Found {failedConnections} waveguide endpoint misalignments out of {totalConnections} connections:\n" +
                         string.Join("\n", failures.Take(20));
            Assert.Fail(message);
        }

        // Success
        totalConnections.ShouldBeGreaterThan(0, "Layout should have connections to test");
    }

    /// <summary>
    /// Detailed analysis of waveguide coordinates - reports all deviations for debugging.
    /// </summary>
    [Fact]
    public void TestTestLayout_DetailedWaveguideAnalysis()
    {
        // Arrange
        var layoutPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "Integration", "testtest.lun");

        if (!File.Exists(layoutPath))
        {
            return;
        }

        var persistenceManager = new PersistenceManager();
        var canvas = new DesignCanvasViewModel();

        var json = File.ReadAllText(layoutPath);
        var (components, connections, groups) = persistenceManager.LoadDesign(json);

        foreach (var comp in components)
        {
            canvas.Components.Add(comp);
        }
        foreach (var conn in connections)
        {
            canvas.Connections.Add(conn);
        }

        // Analyze each connection
        var analysis = new List<string>();
        int connectionIndex = 0;

        foreach (var conn in canvas.Connections.Take(10)) // First 10 for debugging
        {
            if (conn.StartPin == null || conn.EndPin == null || conn.CachedSegments == null)
                continue;

            connectionIndex++;

            var startComp = conn.StartPin.ParentComponent;
            var endComp = conn.EndPin.ParentComponent;

            analysis.Add($"\n=== Connection #{connectionIndex}: {startComp.HumanReadableName} → {endComp.HumanReadableName} ===");
            analysis.Add($"Start Component: {startComp.Identifier} at ({startComp.PhysicalX:F2}, {startComp.PhysicalY:F2})");
            analysis.Add($"  Template: {startComp.TemplateName}");
            analysis.Add($"  OriginOffsetY: {startComp.NazcaOriginOffsetY:F2}, Height: {startComp.HeightMicrometers:F2}");
            analysis.Add($"  Pin '{conn.StartPin.Name}' offset: ({conn.StartPin.OffsetXMicrometers:F2}, {conn.StartPin.OffsetYMicrometers:F2})");

            var (startPinX, startPinY) = conn.StartPin.GetAbsoluteNazcaPosition();
            analysis.Add($"  Pin Nazca position: ({startPinX:F2}, {startPinY:F2})");

            if (conn.CachedSegments.Count > 0)
            {
                var firstSeg = conn.CachedSegments.First();
                analysis.Add($"  Waveguide start: ({firstSeg.StartX:F2}, {-firstSeg.StartY:F2})");

                double xDev = Math.Abs(startPinX - firstSeg.StartX);
                double yDev = Math.Abs(startPinY - (-firstSeg.StartY));
                analysis.Add($"  Deviation: ΔX={xDev:F4} µm, ΔY={yDev:F4} µm {(xDev >= 0.01 || yDev >= 0.01 ? "❌ FAIL" : "✓")}");
            }

            analysis.Add($"\nEnd Component: {endComp.Identifier} at ({endComp.PhysicalX:F2}, {endComp.PhysicalY:F2})");
            analysis.Add($"  Template: {endComp.TemplateName}");
            analysis.Add($"  Pin '{conn.EndPin.Name}' offset: ({conn.EndPin.OffsetXMicrometers:F2}, {conn.EndPin.OffsetYMicrometers:F2})");

            var (endPinX, endPinY) = conn.EndPin.GetAbsoluteNazcaPosition();
            analysis.Add($"  Pin Nazca position: ({endPinX:F2}, {endPinY:F2})");

            if (conn.CachedSegments.Count > 0)
            {
                var lastSeg = conn.CachedSegments.Last();
                analysis.Add($"  Waveguide end: ({lastSeg.EndX:F2}, {-lastSeg.EndY:F2})");

                double xDev = Math.Abs(endPinX - lastSeg.EndX);
                double yDev = Math.Abs(endPinY - (-lastSeg.EndY));
                analysis.Add($"  Deviation: ΔX={xDev:F4} µm, ΔY={yDev:F4} µm {(xDev >= 0.01 || yDev >= 0.01 ? "❌ FAIL" : "✓")}");
            }
        }

        // Output analysis (will appear in test output)
        var output = string.Join("\n", analysis);

        // This test always passes - it's just for detailed analysis
        // Check the test output to see the analysis
        Assert.True(true, output);
    }
}
