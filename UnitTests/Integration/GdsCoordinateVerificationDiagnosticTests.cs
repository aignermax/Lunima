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

namespace UnitTests.Integration;

/// <summary>Coordinate diagnostics report the exact exported pin deviation.</summary>
[Trait("Category", "Slow")]
[Collection("GdsCoordinateVerification")]
public class GdsCoordinateVerificationDiagnosticTests
{
    private readonly ObservableCollection<ComponentTemplate> _library;
    private readonly SimpleNazcaExporter _exporter = new();
    private readonly NazcaCodeParser _parser = new();
    private readonly ITestOutputHelper _output;

    public GdsCoordinateVerificationDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
        _library = new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates());
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
}
