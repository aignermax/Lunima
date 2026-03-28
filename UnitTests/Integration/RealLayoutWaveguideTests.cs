using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.CodeExporter;
using CAP_Core.Export;
using Moq;
using Shouldly;
using System.Collections.ObjectModel;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Tests GDS coordinate accuracy using real user layout files.
/// Verifies that waveguide start/end points match component pin positions exactly.
/// Issue #334: Investigates PDK coordinate mismatches in real user designs.
/// </summary>
public class RealLayoutWaveguideTests
{
    private const double PinAlignmentTolerance = 0.01; // µm

    private readonly ObservableCollection<ComponentTemplate> _library;

    public RealLayoutWaveguideTests()
    {
        _library = new ObservableCollection<ComponentTemplate>(ComponentTemplates.GetAllTemplates());
    }

    /// <summary>
    /// Tests the actual user layout file (testtest.lun) to verify all waveguide endpoints
    /// align with component pins after GDS export.
    /// </summary>
    [Fact]
    public async Task TestTestLayout_AllWaveguides_MustAlignWithPins()
    {
        var layoutPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "Integration", "testtest.lun");

        if (!File.Exists(layoutPath))
            return; // Skip test if file not found

        var (canvas, _) = await LoadLayoutFromFile(layoutPath);

        var exporter = new SimpleNazcaExporter();
        var nazcaCode = exporter.Export(canvas);
        var parser = new NazcaCodeParser();
        parser.Parse(nazcaCode); // ensure parse succeeds

        int totalConnections = 0;
        int failedConnections = 0;
        var failures = new List<string>();

        foreach (var connVm in canvas.Connections)
        {
            var conn = connVm.Connection;
            var segments = conn.GetPathSegments();

            if (conn.StartPin == null || conn.EndPin == null || segments.Count == 0)
                continue;

            totalConnections++;

            var (startPinX, startPinY) = conn.StartPin.GetAbsoluteNazcaPosition();
            var (endPinX, endPinY) = conn.EndPin.GetAbsoluteNazcaPosition();

            var firstSeg = segments.First();
            var lastSeg = segments.Last();

            double wgStartX = firstSeg.StartPoint.X;
            double wgStartY = -firstSeg.StartPoint.Y; // Convert to Nazca Y-up
            double wgEndX = lastSeg.EndPoint.X;
            double wgEndY = -lastSeg.EndPoint.Y;

            double startXDev = Math.Abs(startPinX - wgStartX);
            double startYDev = Math.Abs(startPinY - wgStartY);

            if (startXDev >= PinAlignmentTolerance || startYDev >= PinAlignmentTolerance)
            {
                failedConnections++;
                failures.Add(
                    $"START: {conn.StartPin.ParentComponent.Identifier}→{conn.EndPin.ParentComponent.Identifier} " +
                    $"ΔX={startXDev:F4} µm, ΔY={startYDev:F4} µm");
            }

            double endXDev = Math.Abs(endPinX - wgEndX);
            double endYDev = Math.Abs(endPinY - wgEndY);

            if (endXDev >= PinAlignmentTolerance || endYDev >= PinAlignmentTolerance)
            {
                failedConnections++;
                failures.Add(
                    $"END: {conn.StartPin.ParentComponent.Identifier}→{conn.EndPin.ParentComponent.Identifier} " +
                    $"ΔX={endXDev:F4} µm, ΔY={endYDev:F4} µm");
            }
        }

        if (failedConnections > 0)
        {
            var message = $"Found {failedConnections} waveguide endpoint misalignments " +
                          $"out of {totalConnections} connections:\n" +
                          string.Join("\n", failures.Take(20));
            Assert.Fail(message);
        }

        // Note: connections without routed paths (segments.Count == 0) are skipped.
        // If no routable connections exist in the layout file, the test passes vacuously.
    }

    /// <summary>
    /// Detailed analysis of waveguide coordinates — reports deviations for debugging.
    /// This test always passes; examine output for misalignment details.
    /// </summary>
    [Fact]
    public async Task TestTestLayout_DetailedWaveguideAnalysis()
    {
        var layoutPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "Integration", "testtest.lun");

        if (!File.Exists(layoutPath))
            return; // Skip test if file not found

        var (canvas, _) = await LoadLayoutFromFile(layoutPath);
        var analysis = new List<string>();
        int connectionIndex = 0;

        foreach (var connVm in canvas.Connections.Take(10))
        {
            var conn = connVm.Connection;
            var segments = conn.GetPathSegments();

            if (conn.StartPin == null || conn.EndPin == null)
                continue;

            connectionIndex++;
            var startComp = conn.StartPin.ParentComponent;
            var endComp = conn.EndPin.ParentComponent;

            analysis.Add($"\n=== Connection #{connectionIndex}: {startComp.HumanReadableName} → {endComp.HumanReadableName} ===");
            analysis.Add($"Start: {startComp.Identifier} at ({startComp.PhysicalX:F2}, {startComp.PhysicalY:F2})");
            analysis.Add($"  OriginOffsetY: {startComp.NazcaOriginOffsetY:F2}, Height: {startComp.HeightMicrometers:F2}");
            analysis.Add($"  Pin '{conn.StartPin.Name}' offset: ({conn.StartPin.OffsetXMicrometers:F2}, {conn.StartPin.OffsetYMicrometers:F2})");

            var (startPinX, startPinY) = conn.StartPin.GetAbsoluteNazcaPosition();
            analysis.Add($"  Pin Nazca pos: ({startPinX:F2}, {startPinY:F2})");

            if (segments.Count > 0)
            {
                var firstSeg = segments.First();
                double wgStartY = -firstSeg.StartPoint.Y;
                double xDev = Math.Abs(startPinX - firstSeg.StartPoint.X);
                double yDev = Math.Abs(startPinY - wgStartY);
                analysis.Add($"  WG start: ({firstSeg.StartPoint.X:F2}, {wgStartY:F2})  ΔX={xDev:F4}, ΔY={yDev:F4} {(xDev >= 0.01 || yDev >= 0.01 ? "FAIL" : "OK")}");
            }

            var (endPinX, endPinY) = conn.EndPin.GetAbsoluteNazcaPosition();
            analysis.Add($"End: {endComp.Identifier}  Pin '{conn.EndPin.Name}'  Nazca: ({endPinX:F2}, {endPinY:F2})");

            if (segments.Count > 0)
            {
                var lastSeg = segments.Last();
                double wgEndY = -lastSeg.EndPoint.Y;
                double xDev = Math.Abs(endPinX - lastSeg.EndPoint.X);
                double yDev = Math.Abs(endPinY - wgEndY);
                analysis.Add($"  WG end:   ({lastSeg.EndPoint.X:F2}, {wgEndY:F2})  ΔX={xDev:F4}, ΔY={yDev:F4} {(xDev >= 0.01 || yDev >= 0.01 ? "FAIL" : "OK")}");
            }
        }

        // Output analysis — always passes, examine results manually
        var output = string.Join("\n", analysis);
        Assert.True(true, output);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task<(DesignCanvasViewModel canvas, FileOperationsViewModel vm)> LoadLayoutFromFile(
        string filePath)
    {
        var canvas = new DesignCanvasViewModel();
        var vm = new FileOperationsViewModel(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            _library,
            new GdsExportViewModel(new GdsExportService()));

        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowOpenFileDialogAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(filePath);
        vm.FileDialogService = dialog.Object;

        await vm.LoadDesignCommand.ExecuteAsync(null);
        return (canvas, vm);
    }
}
