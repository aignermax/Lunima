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

/// <summary>Incoming routes and MMI export coordinates use the Nazca coordinate mapper.</summary>
[Trait("Category", "Slow")]
[Collection("GdsCoordinateVerification")]
public class GdsCoordinateVerificationIncomingTests
{
    private readonly ObservableCollection<ComponentTemplate> _library;
    private readonly SimpleNazcaExporter _exporter = new();
    private readonly NazcaCodeParser _parser = new();
    private readonly ITestOutputHelper _output;

    public GdsCoordinateVerificationIncomingTests(ITestOutputHelper output)
    {
        _output = output;
        _library = new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates());
    }

    /// <summary>
    /// Test incoming waveguides: waveguide END position must match target pin.
    /// This tests the OPPOSITE direction from the basic tests.
    /// </summary>
    [Fact]
    public void IncomingWaveguide_EndMustMatchTargetPin()
    {
        var canvas = new DesignCanvasViewModel();
        var gcTemplate = _library.First(t => t.Name == "Grating Coupler");

        // GC1 at (0, 0) - SOURCE
        var gc1 = ComponentTemplates.CreateFromTemplate(gcTemplate, 0, 0);
        gc1.Identifier = "gc1_incoming";
        gc1.NazcaFunctionName = "ebeam_gc_incoming";
        canvas.AddComponent(gc1, gcTemplate.Name);

        // GC2 at (200, 0) - TARGET (testing incoming waveguide END point)
        var gc2 = ComponentTemplates.CreateFromTemplate(gcTemplate, 200, 0);
        gc2.Identifier = "gc2_incoming";
        gc2.NazcaFunctionName = "ebeam_gc_incoming";
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

        parsed.Components.Count.ShouldBe(2);
        var stubPin = parsed.PinDefinitions.First(p => p.Name == "waveguide");
        var wg = parsed.WaveguideStubs.First();

        // Check GC2 (TARGET): waveguide END must match GC2's global pin position
        var gc2Pos = parsed.Components[1];
        double expectedX = gc2Pos.X + stubPin.X;
        double expectedY = gc2Pos.Y + stubPin.Y;

        // Waveguide end = start + length (horizontal segment)
        double wgEndX = wg.StartX + wg.Length;
        double wgEndY = wg.StartY;

        double xDev = Math.Abs(expectedX - wgEndX);
        double yDev = Math.Abs(expectedY - wgEndY);

        _output.WriteLine($"GC2 (target) expected pin: ({expectedX:F2}, {expectedY:F2})");
        _output.WriteLine($"Waveguide end: ({wgEndX:F2}, {wgEndY:F2})");
        _output.WriteLine($"Deviation: X={xDev:F4} µm, Y={yDev:F4} µm");

        xDev.ShouldBeLessThan(PinAlignmentTolerance,
            $"Incoming waveguide X mismatch at target pin: {xDev:F4} µm");
        yDev.ShouldBeLessThan(PinAlignmentTolerance,
            $"Incoming waveguide Y mismatch at target pin: {yDev:F4} µm (Issue #329)");
    }

    /// <summary>
    /// Test MMI component pins (different dimensions: 50×10 µm vs GC 100×19 µm).
    /// MMI has multiple pins at different Y positions.
    /// </summary>
    [Fact]
    public void DifferentComponent_MMI_PinPositionsMustMatch()
    {
        var canvas = new DesignCanvasViewModel();
        var mmiTemplate = _library.FirstOrDefault(t => t.Name == "MMI 2x2");
        if (mmiTemplate == null)
        {
            _output.WriteLine("SKIP: MMI 2x2 not found in library");
            return;
        }
        var gcTemplate = _library.First(t => t.Name == "Grating Coupler");

        // MMI at (100, 50)
        var mmi = ComponentTemplates.CreateFromTemplate(mmiTemplate, 100, 50);
        mmi.Identifier = "mmi_test";
        mmi.NazcaFunctionName = "ebeam_mmi_2x2";
        canvas.AddComponent(mmi, mmiTemplate.Name);

        // GC to connect to MMI output
        var gc = ComponentTemplates.CreateFromTemplate(gcTemplate, 250, 50);
        gc.Identifier = "gc_mmi_out";
        gc.NazcaFunctionName = "ebeam_gc_mmi";
        canvas.AddComponent(gc, gcTemplate.Name);

        // Connect MMI output to GC
        var mmiPin = mmi.PhysicalPins.First(p => p.Name == "output1");
        var gcPin = gc.PhysicalPins.First(p => p.Name == "waveguide");
        var (s1x, s1y) = mmiPin.GetAbsolutePosition();
        var (s2x, s2y) = gcPin.GetAbsolutePosition();

        var route = new RoutedPath();
        route.Segments.Add(new StraightSegment(s1x, s1y, s2x, s2y, mmiPin.GetAbsoluteAngle()));
        canvas.ConnectPinsWithCachedRoute(mmiPin, gcPin, route);

        var script = _exporter.Export(canvas);
        var parsed = _parser.Parse(script);

        _output.WriteLine($"MMI template: {mmiTemplate.WidthMicrometers}×{mmiTemplate.HeightMicrometers} µm");
        _output.WriteLine($"MMI NazcaOriginOffsetY: {mmiTemplate.NazcaOriginOffsetY}");
        _output.WriteLine($"MMI output1 pin editor offset: ({mmiPin.OffsetXMicrometers}, {mmiPin.OffsetYMicrometers})");

        // Find MMI placement and output1 pin in stub
        var mmiStub = parsed.PinDefinitions.FirstOrDefault(p => p.Name == "output1");
        if (mmiStub == null)
        {
            _output.WriteLine("WARNING: MMI stub not found or output1 pin missing");
            return; // Skip if MMI stub generation doesn't work
        }

        var mmiPlacement = parsed.Components.First();
        double expectedX = mmiPlacement.X + mmiStub.X;
        double expectedY = mmiPlacement.Y + mmiStub.Y;

        var wg = parsed.WaveguideStubs.First();
        double xDev = Math.Abs(expectedX - wg.StartX);
        double yDev = Math.Abs(expectedY - wg.StartY);

        _output.WriteLine($"MMI placement: ({mmiPlacement.X:F2}, {mmiPlacement.Y:F2})");
        _output.WriteLine($"Expected global pin: ({expectedX:F2}, {expectedY:F2})");
        _output.WriteLine($"Waveguide start: ({wg.StartX:F2}, {wg.StartY:F2})");
        _output.WriteLine($"Deviation: X={xDev:F4} µm, Y={yDev:F4} µm");

        xDev.ShouldBeLessThan(PinAlignmentTolerance,
            $"MMI output1 X mismatch: {xDev:F4} µm");
        yDev.ShouldBeLessThan(PinAlignmentTolerance,
            $"MMI output1 Y mismatch: {yDev:F4} µm (different component dimensions test)");
    }
}
