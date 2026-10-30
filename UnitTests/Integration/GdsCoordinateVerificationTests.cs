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
///
/// Scope: this is a STRUCTURAL check that the exporter routes its coordinates through
/// <see cref="NazcaCoordinateMapper"/> (it asserts against the same mapper the exporter
/// uses, so it cannot catch a wrong mapper FORMULA). Formula correctness is proven by
/// NazcaCoordinateMapperTests (hand-computed expectations) and by GdsExportAlignmentTests
/// (verified against the real nazca engine that writes the GDS).
/// </summary>
[Trait("Category", "Slow")]
[Collection("GdsCoordinateVerification")]
public class GdsCoordinateVerificationTests
{
    private readonly ObservableCollection<ComponentTemplate> _library;
    private readonly SimpleNazcaExporter _exporter = new();
    private readonly NazcaCodeParser _parser = new();
    private readonly ITestOutputHelper _output;

    public GdsCoordinateVerificationTests(ITestOutputHelper output)
    {
        _output = output;
        _library = new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates());
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
}
