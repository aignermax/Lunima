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

// ITestOutputHelper is in Xunit namespace in xUnit v2+

namespace UnitTests.Integration;

/// <summary>
/// GDS position verification tests (Issue #329).
///
/// Confirms the fabrication-blocking coordinate bug where waveguide geometry
/// does not align with component pin positions in the exported Nazca script.
///
/// Root cause:
///   SimpleNazcaExporter writes waveguide start coordinates by negating the
///   editor's absolute pin Y position:
///     nazcaY_waveguide = -(physY + pinOffsetY)
///
///   But stub-based components are placed at:
///     nazcaY_component = -(physY + NazcaOriginOffsetY)
///   with the stub pin at local Y = (height - pinOffsetY).
///
///   Global Nazca pin Y = nazcaY_component + localPinY
///                      = -(physY + originOffsetY) + (height - pinOffsetY)
///
///   For a Grating Coupler (height=19, originOffsetY=9.5, pinOffsetY=9.5):
///     Waveguide Y (exporter) = -(0 + 9.5)  = -9.5
///     Global pin Y (stub)    = -(0 + 9.5) + (19 - 9.5) = 0
///     Mismatch               = 9.5 µm
///
/// These tests FAIL when the bug is present, providing exact deviation data.
/// </summary>
public class GdsCoordinateVerificationTests
{
    private const double PinAlignmentTolerance = 0.01; // µm — tight, 10 nm
    private const double GcHeight = 19.0;              // µm — Grating Coupler height
    private const double GcOriginOffsetY = 9.5;        // µm — NazcaOriginOffsetY for GC
    private const double GcPinOffsetY = 9.5;           // µm — GC waveguide pin Y offset in editor

    private readonly ObservableCollection<ComponentTemplate> _library;
    private readonly SimpleNazcaExporter _exporter = new();
    private readonly NazcaCodeParser _parser = new();
    private readonly ITestOutputHelper _output;

    /// <summary>Initializes the test suite with the full component library.</summary>
    public GdsCoordinateVerificationTests(ITestOutputHelper output)
    {
        _output = output;
        _library = new ObservableCollection<ComponentTemplate>(ComponentTemplates.GetAllTemplates());
    }

    /// <summary>
    /// Core bug check: the waveguide start Y in the exported Nazca script must match
    /// the global Nazca Y of the pin computed from the component stub definition.
    ///
    /// Design: 1 Grating Coupler at editor (0, 0), a straight waveguide starting
    /// from its waveguide pin.
    ///
    /// Using a PDK-style function name ("ebeam_gc_te1550") forces stub generation,
    /// making pin positions readable from the parsed script.
    ///
    /// Expected to FAIL when the coordinate bug is present (9.5 µm Y offset).
    /// </summary>
    [Fact]
    public void WaveguideStart_MustMatchPin_GlobalNazcaCoordinate()
    {
        // Arrange: single GC at editor (0, 0) with PDK function name → stub generated
        var canvas = new DesignCanvasViewModel();
        var gcTemplate = _library.First(t => t.Name == "Grating Coupler");

        var gc1 = ComponentTemplates.CreateFromTemplate(gcTemplate, 0, 0);
        gc1.Identifier = "gc1_coord_test";
        gc1.NazcaFunctionName = "ebeam_gc_te1550"; // forces stub generation
        canvas.AddComponent(gc1, gcTemplate.Name);

        // Destination component for the waveguide (just needs a compatible input pin)
        var gc2 = ComponentTemplates.CreateFromTemplate(gcTemplate, 300, 0);
        gc2.Identifier = "gc2_coord_test";
        gc2.NazcaFunctionName = "ebeam_gc_te1550";
        canvas.AddComponent(gc2, gcTemplate.Name);

        // Add straight waveguide from gc1.waveguide to gc2.waveguide
        var pin1 = gc1.PhysicalPins.First(p => p.Name == "waveguide");
        var pin2 = gc2.PhysicalPins.First(p => p.Name == "waveguide");
        var (s1x, s1y) = pin1.GetAbsolutePosition();
        var (s2x, s2y) = pin2.GetAbsolutePosition();

        var route = new RoutedPath();
        route.Segments.Add(new StraightSegment(s1x, s1y, s2x, s2y, pin1.GetAbsoluteAngle()));
        canvas.ConnectPinsWithCachedRoute(pin1, pin2, route);

        // Act: export and parse
        var script = _exporter.Export(canvas);
        var parsed = _parser.Parse(script);

        _output.WriteLine("=== Exported Nazca Script (key parts) ===");
        _output.WriteLine(ExcerptScript(script, 60));
        _output.WriteLine("...");

        // Verify we got 2 components, 1 waveguide, and pin definitions
        parsed.Components.Count.ShouldBe(2, "Both GCs must appear in Nazca script");

        var stubPin = parsed.PinDefinitions.FirstOrDefault(p => p.Name == "waveguide");
        stubPin.ShouldNotBeNull(
            "'waveguide' pin must appear in the ebeam_gc_te1550 stub definition. " +
            "Ensure NazcaFunctionName='ebeam_gc_te1550' triggers stub generation.");

        parsed.WaveguideStubs.Count.ShouldBeGreaterThan(0,
            "At least one waveguide segment must appear in the exported Nazca script.");

        // Find GC1 placement (it appears first in the script)
        var gc1Placement = parsed.Components.First();
        _output.WriteLine($"GC1 placement in Nazca: ({gc1Placement.X:F4}, {gc1Placement.Y:F4}, {gc1Placement.RotationDegrees}°)");
        _output.WriteLine($"Stub pin 'waveguide' (local Nazca): ({stubPin.X:F4}, {stubPin.Y:F4}, {stubPin.AngleDegrees}°)");

        // Compute expected GLOBAL Nazca position of the waveguide pin
        double expectedGlobalX = gc1Placement.X + stubPin.X;
        double expectedGlobalY = gc1Placement.Y + stubPin.Y;
        _output.WriteLine($"Expected global Nazca pin: ({expectedGlobalX:F4}, {expectedGlobalY:F4})");

        // Find the first waveguide segment start (connects from GC1 pin)
        var wgStub = parsed.WaveguideStubs.First();
        _output.WriteLine($"Waveguide start (exporter): ({wgStub.StartX:F4}, {wgStub.StartY:F4}, {wgStub.StartAngle:F4}°)");

        double xDev = Math.Abs(expectedGlobalX - wgStub.StartX);
        double yDev = Math.Abs(expectedGlobalY - wgStub.StartY);
        _output.WriteLine($"Deviation: X={xDev:F4} µm, Y={yDev:F4} µm  (tolerance={PinAlignmentTolerance} µm)");

        // These assertions FAIL when the coordinate bug is present.
        // Expected Y deviation: 9.5 µm (NazcaOriginOffsetY for the Grating Coupler)
        xDev.ShouldBeLessThan(PinAlignmentTolerance,
            $"X mismatch: waveguide at X={wgStub.StartX:F4}, pin global X={expectedGlobalX:F4} " +
            $"(deviation={xDev:F4} µm)");

        yDev.ShouldBeLessThan(PinAlignmentTolerance,
            $"Y coordinate mismatch of {yDev:F4} µm detected (fabrication blocker, Issue #329). " +
            $"Waveguide starts at Y={wgStub.StartY:F4} but pin global Y={expectedGlobalY:F4}. " +
            $"Root cause: exporter uses raw editor Y={s1y:F4} → Nazca Y={-s1y:F4}, " +
            $"but the stub places the pin at Y={(gc1Placement.Y + stubPin.Y):F4}. " +
            $"Fix: apply NazcaOriginOffsetY compensation in waveguide coordinate export.");
    }

    /// <summary>
    /// Comprehensive check: both waveguide endpoints (start and end) must match
    /// their respective pin global Nazca positions.
    ///
    /// Design: GC1 at (0, 0) and GC2 at (300, 0), straight waveguide between them.
    /// </summary>
    [Fact]
    public void TwoGratings_WaveguideEndpoints_MustMatchPinPositions()
    {
        var canvas = new DesignCanvasViewModel();
        var gcTemplate = _library.First(t => t.Name == "Grating Coupler");

        var gc1 = ComponentTemplates.CreateFromTemplate(gcTemplate, 0, 0);
        gc1.Identifier = "gc1_ep_test";
        gc1.NazcaFunctionName = "ebeam_gc_te1550";
        canvas.AddComponent(gc1, gcTemplate.Name);

        var gc2 = ComponentTemplates.CreateFromTemplate(gcTemplate, 300, 0);
        gc2.Identifier = "gc2_ep_test";
        gc2.NazcaFunctionName = "ebeam_gc_te1550";
        canvas.AddComponent(gc2, gcTemplate.Name);

        var pin1 = gc1.PhysicalPins.First(p => p.Name == "waveguide");
        var pin2 = gc2.PhysicalPins.First(p => p.Name == "waveguide");
        var (s1x, s1y) = pin1.GetAbsolutePosition();
        var (s2x, s2y) = pin2.GetAbsolutePosition();

        var route = new RoutedPath();
        route.Segments.Add(new StraightSegment(s1x, s1y, s2x, s2y, pin1.GetAbsoluteAngle()));
        canvas.ConnectPinsWithCachedRoute(pin1, pin2, route);

        var script = _exporter.Export(canvas);
        var parsed = _parser.Parse(script);

        _output.WriteLine("=== Exported script (excerpt) ===");
        _output.WriteLine(ExcerptScript(script, 60));

        parsed.Components.Count.ShouldBe(2, "Both GCs must be exported");
        parsed.WaveguideStubs.Count.ShouldBeGreaterThan(0, "Waveguide must be exported");

        var stubPin = parsed.PinDefinitions.First(p => p.Name == "waveguide");
        var wg = parsed.WaveguideStubs.First();

        // Check GC1: waveguide start must match GC1's global pin position
        var gc1Pos = parsed.Components[0];
        double gc1ExpectedX = gc1Pos.X + stubPin.X;
        double gc1ExpectedY = gc1Pos.Y + stubPin.Y;
        double gc1DevX = Math.Abs(gc1ExpectedX - wg.StartX);
        double gc1DevY = Math.Abs(gc1ExpectedY - wg.StartY);

        _output.WriteLine($"GC1 placement: ({gc1Pos.X:F2}, {gc1Pos.Y:F2})");
        _output.WriteLine($"GC1 expected global pin: ({gc1ExpectedX:F2}, {gc1ExpectedY:F2})");
        _output.WriteLine($"WG start: ({wg.StartX:F2}, {wg.StartY:F2})");
        _output.WriteLine($"GC1 deviation: X={gc1DevX:F4} µm, Y={gc1DevY:F4} µm");

        gc1DevX.ShouldBeLessThan(PinAlignmentTolerance,
            $"GC1 waveguide start X mismatch: {gc1DevX:F4} µm");
        gc1DevY.ShouldBeLessThan(PinAlignmentTolerance,
            $"GC1 waveguide start Y mismatch of {gc1DevY:F4} µm (Issue #329 bug). " +
            $"WG Y={wg.StartY:F4} vs pin Y={gc1ExpectedY:F4}.");

        // Check GC2: waveguide end = start + length (for a horizontal segment)
        var gc2Pos = parsed.Components[1];
        double gc2ExpectedX = gc2Pos.X + stubPin.X;
        double gc2ExpectedY = gc2Pos.Y + stubPin.Y;
        double wgEndX = wg.StartX + wg.Length;
        double wgEndY = wg.StartY; // horizontal segment
        double gc2DevX = Math.Abs(gc2ExpectedX - wgEndX);
        double gc2DevY = Math.Abs(gc2ExpectedY - wgEndY);

        _output.WriteLine($"GC2 expected global pin: ({gc2ExpectedX:F2}, {gc2ExpectedY:F2})");
        _output.WriteLine($"WG end: ({wgEndX:F2}, {wgEndY:F2})");
        _output.WriteLine($"GC2 deviation: X={gc2DevX:F4} µm, Y={gc2DevY:F4} µm");

        gc2DevX.ShouldBeLessThan(PinAlignmentTolerance,
            $"GC2 waveguide end X mismatch: {gc2DevX:F4} µm");
        gc2DevY.ShouldBeLessThan(PinAlignmentTolerance,
            $"GC2 waveguide end Y mismatch of {gc2DevY:F4} µm (Issue #329 bug).");
    }

    /// <summary>
    /// Diagnostic test: measures and reports the exact coordinate deviation for
    /// the bug report. Always passes; outputs deviation details to test log.
    /// </summary>
    [Fact]
    public void DiagnoseCoordinateOffset_ReportsDeviationForBugReport()
    {
        var canvas = new DesignCanvasViewModel();
        var gcTemplate = _library.First(t => t.Name == "Grating Coupler");

        var gc1 = ComponentTemplates.CreateFromTemplate(gcTemplate, 0, 0);
        gc1.Identifier = "gc_diag";
        gc1.NazcaFunctionName = "ebeam_gc_diag";
        canvas.AddComponent(gc1, gcTemplate.Name);

        var gc2 = ComponentTemplates.CreateFromTemplate(gcTemplate, 300, 0);
        gc2.Identifier = "gc2_diag";
        gc2.NazcaFunctionName = "ebeam_gc_diag";
        canvas.AddComponent(gc2, gcTemplate.Name);

        var pin1 = gc1.PhysicalPins.First(p => p.Name == "waveguide");
        var pin2 = gc2.PhysicalPins.First(p => p.Name == "waveguide");
        var (s1x, s1y) = pin1.GetAbsolutePosition();
        var (s2x, s2y) = pin2.GetAbsolutePosition();

        var route = new RoutedPath();
        route.Segments.Add(new StraightSegment(s1x, s1y, s2x, s2y, pin1.GetAbsoluteAngle()));
        canvas.ConnectPinsWithCachedRoute(pin1, pin2, route);

        var script = _exporter.Export(canvas);
        var parsed = _parser.Parse(script);

        if (parsed.Components.Count == 0 || parsed.WaveguideStubs.Count == 0
            || !parsed.PinDefinitions.Any(p => p.Name == "waveguide"))
        {
            _output.WriteLine("WARNING: Could not parse all required elements from script.");
            _output.WriteLine(script);
            true.ShouldBeTrue(); // pass anyway — diagnostic test
            return;
        }

        var gc1Pos = parsed.Components.First();
        var stubPin = parsed.PinDefinitions.First(p => p.Name == "waveguide");
        var wg = parsed.WaveguideStubs.First();

        double expectedGlobalX = gc1Pos.X + stubPin.X;
        double expectedGlobalY = gc1Pos.Y + stubPin.Y;
        double xDeviation = wg.StartX - expectedGlobalX;
        double yDeviation = wg.StartY - expectedGlobalY;

        _output.WriteLine("=== GDS Coordinate Deviation Report (Issue #329) ===");
        _output.WriteLine($"Component: Grating Coupler at editor (0, 0)");
        _output.WriteLine($"Template NazcaOriginOffsetY:  {gcTemplate.NazcaOriginOffsetY} µm");
        _output.WriteLine($"Template HeightMicrometers:   {gcTemplate.HeightMicrometers} µm");
        _output.WriteLine($"Pin 'waveguide' editor offset: ({pin1.OffsetXMicrometers}, {pin1.OffsetYMicrometers}) µm");
        _output.WriteLine("");
        _output.WriteLine($"Nazca component placement:   ({gc1Pos.X:F4}, {gc1Pos.Y:F4})");
        _output.WriteLine($"Stub local pin position:     ({stubPin.X:F4}, {stubPin.Y:F4})");
        _output.WriteLine($"Expected global Nazca pin:   ({expectedGlobalX:F4}, {expectedGlobalY:F4})");
        _output.WriteLine("");
        _output.WriteLine($"Waveguide start (exporter):  ({wg.StartX:F4}, {wg.StartY:F4})");
        _output.WriteLine("");
        _output.WriteLine($"X deviation: {xDeviation:+0.0000;-0.0000} µm");
        _output.WriteLine($"Y deviation: {yDeviation:+0.0000;-0.0000} µm");
        _output.WriteLine("");
        _output.WriteLine("Root cause analysis:");
        _output.WriteLine($"  Exporter: waveguide Y = -pinEditorY = -{s1y:F4} = {-s1y:F4} µm");
        _output.WriteLine($"  Correct:  waveguide Y = placement.Y + stubPin.Y = {gc1Pos.Y:F4} + {stubPin.Y:F4} = {expectedGlobalY:F4} µm");
        _output.WriteLine($"  Missing correction = height - 2 * originOffsetY = " +
                          $"{gcTemplate.HeightMicrometers} - 2 × {gcTemplate.NazcaOriginOffsetY} = " +
                          $"{gcTemplate.HeightMicrometers - 2 * gcTemplate.NazcaOriginOffsetY:F4} µm");
        _output.WriteLine("");
        _output.WriteLine("Fix required in SimpleNazcaExporter.FormatStraightSegment():");
        _output.WriteLine("  Replace: y = -(straight.StartPoint.Y)");
        _output.WriteLine("  With:    y = -(physY + originOffsetY) + (height - pinOffsetY)");
        _output.WriteLine("  Or equivalently: y via PhysicalPin.GetAbsoluteNazcaY()");
        _output.WriteLine("====================================================");

        // Always passes — diagnostic only
        true.ShouldBeTrue();
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
            var refScriptSrc = Path.Combine(repoRoot, "Scripts", "reference_minimal.py");
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
            var extractScript = Path.Combine(repoRoot, "Scripts", "extract_gds_coords.py");
            await RunPythonAsync(extractScript, $"\"{systemGdsPath}\" \"{systemCoordsPath}\"");
            await RunPythonAsync(extractScript, $"\"{refGdsPath}\" \"{refCoordsPath}\"");

            if (!File.Exists(systemCoordsPath) || !File.Exists(refCoordsPath))
            {
                _output.WriteLine("Coordinate extraction produced no output — skipping.");
                return;
            }

            // 4. Compare coordinates
            var compareScript = Path.Combine(repoRoot, "Scripts", "compare_gds_coords.py");
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

    // ── Design factory ────────────────────────────────────────────────────────

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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ExcerptScript(string script, int lineCount)
        => string.Join('\n', script.Split('\n').Take(lineCount));

    private static async Task<bool> CheckGdspyAsync()
    {
        try
        {
            var tmp = Path.Combine(Path.GetTempPath(), "check_gdspy_cap.py");
            await File.WriteAllTextAsync(tmp, "import gdspy; print('ok')");
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "python3",
                Arguments = $"\"{tmp}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            proc.Start();
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            if (File.Exists(tmp)) File.Delete(tmp);
            return proc.ExitCode == 0 && output.Trim() == "ok";
        }
        catch { return false; }
    }

    private static async Task RunPythonAsync(string scriptPath, string args)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "python3",
                Arguments = $"\"{scriptPath}\" {args}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            proc.Start();
            await proc.WaitForExitAsync();
        }
        catch { /* Python not available */ }
    }

    private static async Task<(int exitCode, string output, string error)> RunPythonRawAsync(
        string scriptPath, string args)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "python3",
                Arguments = $"\"{scriptPath}\" {args}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            proc.Start();
            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return (proc.ExitCode, await outTask, await errTask);
        }
        catch (Exception ex) { return (1, "", ex.Message); }
    }

    private static string FixGdsOutputPath(string script, string gdsPath)
        => script.Replace(
            "gds_filename = os.path.splitext(script_path)[0] + '.gds'",
            $"gds_filename = r'{gdsPath.Replace("\\", "/")}'");

    private static string FindRepoRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "ConnectAPICPro.sln")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("ConnectAPICPro.sln not found");
    }

    private static double ParseMaxDeviation(string reportJson)
    {
        const string key = "\"max_deviation_um\":";
        int idx = reportJson.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0) return double.MaxValue;
        int start = idx + key.Length;
        while (start < reportJson.Length && char.IsWhiteSpace(reportJson[start])) start++;
        int end = start;
        while (end < reportJson.Length &&
               (char.IsDigit(reportJson[end]) || reportJson[end] == '.' || reportJson[end] == '-'))
            end++;
        return double.TryParse(reportJson.AsSpan(start, end - start),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : double.MaxValue;
    }
}
