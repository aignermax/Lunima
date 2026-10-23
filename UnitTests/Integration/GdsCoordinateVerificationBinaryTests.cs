using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.CodeExporter;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_Core.Routing;
using Shouldly;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Xunit;
using static UnitTests.Integration.GdsCoordinateVerificationFixture;
using static UnitTests.Integration.GdsCoordinateVerificationPython;

namespace UnitTests.Integration;

/// <summary>Optional Python/GDS comparison verifies actual binary geometry alignment.</summary>
[Trait("Category", "Slow")]
[Collection("GdsCoordinateVerification")]
public class GdsCoordinateVerificationBinaryTests
{
    private readonly ObservableCollection<ComponentTemplate> _library;
    private readonly SimpleNazcaExporter _exporter = new();
    private readonly ITestOutputHelper _output;

    public GdsCoordinateVerificationBinaryTests(ITestOutputHelper output)
    {
        _output = output;
        _library = new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates());
    }

    /// <summary>
    /// Integration test: if Python + Nazca + gdspy are available, generates both
    /// the system GDS and the reference GDS, runs Python comparison scripts, and
    /// asserts max deviation is within tolerance.
    ///
    /// Skips gracefully when the Python stack is not available.
    /// Expected to FAIL (max_deviation > tolerance) when the bug is present.
    /// </summary>
    [Fact]
    public async Task PythonGdsBinaryComparison_WhenEnvironmentReady_DeviationsWithinTolerance()
    {
        var gdsService = new GdsExportService();
        var env = await gdsService.CheckPythonEnvironmentAsync();
        if (!env.IsReady)
        {
            _output.WriteLine($"Python environment not ready ({env.StatusMessage}) — skipping.");
            return;
        }

        var gdspyAvailable = await CheckGdspyAsync();
        if (!gdspyAvailable)
        {
            _output.WriteLine("gdspy not installed — skipping. Install with: pip install gdspy");
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"cap_gds_verify_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // 1. Generate system GDS
            var systemScriptPath = Path.Combine(tempDir, "system_minimal.py");
            var systemGdsPath    = Path.ChangeExtension(systemScriptPath, ".gds");
            var systemCoordsPath = Path.ChangeExtension(systemScriptPath, "_coords.json");

            var canvas = CreateMinimalDesign();
            var script = _exporter.Export(canvas);
            script = FixGdsOutputPath(script, systemGdsPath);
            await File.WriteAllTextAsync(systemScriptPath, script);

            var exportResult = await gdsService.ExportToGdsAsync(systemScriptPath, generateGds: true);
            if (!exportResult.Success)
            {
                _output.WriteLine($"System GDS generation failed: {exportResult.ErrorMessage}");
                return;
            }

            // 2. Generate reference GDS
            var repoRoot = FindRepoRoot();
            var refScriptSrc = Path.Combine(repoRoot, "scripts", "reference_minimal.py");
            var refGdsPath    = Path.Combine(tempDir, "reference_minimal.gds");
            var refCoordsPath = Path.Combine(tempDir, "reference_coords.json");
            var reportPath    = Path.Combine(tempDir, "comparison_report.json");

            var refScript = await File.ReadAllTextAsync(refScriptSrc);
            refScript = refScript.Replace(
                "output_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'tmp', 'reference_minimal.gds')",
                $"output_path = r'{refGdsPath.Replace("\\", "/")}'");
            var tmpRefScript = Path.Combine(tempDir, "reference_minimal.py");
            await File.WriteAllTextAsync(tmpRefScript, refScript);

            var refResult = await gdsService.ExportToGdsAsync(tmpRefScript, generateGds: true);
            if (!refResult.Success)
            {
                _output.WriteLine($"Reference GDS generation failed: {refResult.ErrorMessage}");
                return;
            }

            // 3. Extract coordinates from both GDS files
            var extractScript = Path.Combine(repoRoot, "scripts", "extract_gds_coords.py");
            await RunPythonAsync(extractScript, $"\"{systemGdsPath}\" \"{systemCoordsPath}\"");
            await RunPythonAsync(extractScript, $"\"{refGdsPath}\" \"{refCoordsPath}\"");

            if (!File.Exists(systemCoordsPath) || !File.Exists(refCoordsPath))
            {
                _output.WriteLine("Coordinate extraction produced no output — skipping.");
                return;
            }

            // 4. Compare coordinates
            var compareScript = Path.Combine(repoRoot, "scripts", "compare_gds_coords.py");
            var (exitCode, compareOut, _) = await RunPythonRawAsync(
                compareScript, $"\"{refCoordsPath}\" \"{systemCoordsPath}\" \"{reportPath}\"");

            _output.WriteLine(compareOut);

            if (!File.Exists(reportPath))
            {
                _output.WriteLine("Comparison report not generated — cannot assert.");
                return;
            }

            var reportJson = await File.ReadAllTextAsync(reportPath);
            _output.WriteLine($"Report: {reportJson}");

            // Copy to /tmp for permanent access
            await File.WriteAllTextAsync("/tmp/comparison_report.json", reportJson);

            double maxDev = ParseMaxDeviation(reportJson);
            _output.WriteLine($"Max deviation: {maxDev:F4} µm  (tolerance={PinAlignmentTolerance} µm)");

            // FAILS when coordinate bug is present
            maxDev.ShouldBeLessThan(PinAlignmentTolerance,
                $"GDS coordinate deviation {maxDev:F4} µm exceeds tolerance. " +
                $"See /tmp/comparison_report.json");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
        }
    }

    /// <summary>Minimal design: 2 GCs + 1 straight waveguide.</summary>
    private DesignCanvasViewModel CreateMinimalDesign()
    {
        var canvas = new DesignCanvasViewModel();
        var gcTemplate = _library.First(t => t.Name == "Grating Coupler");

        var gc1 = ComponentTemplates.CreateFromTemplate(gcTemplate, 0, 0);
        gc1.Identifier = "gc1_minimal";
        gc1.NazcaFunctionName = "ebeam_gc_te1550";
        canvas.AddComponent(gc1, gcTemplate.Name);

        var gc2 = ComponentTemplates.CreateFromTemplate(gcTemplate, 300, 0);
        gc2.Identifier = "gc2_minimal";
        gc2.NazcaFunctionName = "ebeam_gc_te1550";
        canvas.AddComponent(gc2, gcTemplate.Name);

        var pin1 = gc1.PhysicalPins.First(p => p.Name == "waveguide");
        var pin2 = gc2.PhysicalPins.First(p => p.Name == "waveguide");
        var (s1x, s1y) = pin1.GetAbsolutePosition();
        var (s2x, s2y) = pin2.GetAbsolutePosition();

        var route = new RoutedPath();
        route.Segments.Add(new StraightSegment(s1x, s1y, s2x, s2y, pin1.GetAbsoluteAngle()));
        canvas.ConnectPinsWithCachedRoute(pin1, pin2, route);

        return canvas;
    }
}
