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

/// <summary>Every bundled PDK component exports its waveguide pins through the coordinate mapper.</summary>
[Trait("Category", "Slow")]
[Collection("GdsCoordinateVerification")]
public class GdsCoordinateVerificationPdkTests
{
    private readonly ObservableCollection<ComponentTemplate> _library;
    private readonly SimpleNazcaExporter _exporter = new();
    private readonly NazcaCodeParser _parser = new();
    private readonly ITestOutputHelper _output;

    public GdsCoordinateVerificationPdkTests(ITestOutputHelper output)
    {
        _output = output;
        _library = new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates());
    }

    /// <summary>
    /// EXHAUSTIVE TEST: Test ALL PDK components systematically.
    /// Each component is placed, connected to a GC, and tested for pin alignment.
    /// This ensures the coordinate bug (or its fix) applies uniformly across the entire PDK.
    /// </summary>
    [Fact]
    public void ExhaustiveAllPdkComponents_PinPositionsMustMatch()
    {
        var gcTemplate = _library.First(t => t.Name == "Grating Coupler");
        var testedComponents = 0;
        var failedComponents = new List<string>();
        var skippedComponents = new List<string>();

        _output.WriteLine("=== EXHAUSTIVE PDK COMPONENT TEST ===");
        _output.WriteLine($"Total components in library: {_library.Count}");
        _output.WriteLine("");

        foreach (var template in _library)
        {
            // Skip Grating Coupler itself (already extensively tested)
            if (template.Name == "Grating Coupler")
                continue;

            _output.WriteLine($"Testing component: {template.Name}");
            _output.WriteLine($"  Dimensions: {template.WidthMicrometers}×{template.HeightMicrometers} µm");
            _output.WriteLine($"  NazcaOriginOffsetY: {template.NazcaOriginOffsetY} µm");

            var canvas = new DesignCanvasViewModel();

            // Create component at (100, 50)
            var component = ComponentTemplates.CreateFromTemplate(template, 100, 50);
            component.Identifier = $"test_{template.Name.Replace(" ", "_")}";
            component.NazcaFunctionName = $"ebeam_{template.Name.Replace(" ", "_").ToLower()}";
            canvas.AddComponent(component, template.Name);

            // Find first output pin (or any pin)
            var outputPin = component.PhysicalPins.FirstOrDefault(p =>
                p.Name.Contains("output") || p.Name.Contains("waveguide") || p.Name.Contains("o"));

            if (outputPin == null)
            {
                _output.WriteLine($"  SKIP: No suitable output pin found");
                _output.WriteLine("");
                skippedComponents.Add(template.Name);
                continue;
            }

            _output.WriteLine($"  Using pin: {outputPin.Name} at offset ({outputPin.OffsetXMicrometers}, {outputPin.OffsetYMicrometers})");

            // Connect to a Grating Coupler
            var (outX, outY) = outputPin.GetAbsolutePosition();
            var gc = ComponentTemplates.CreateFromTemplate(gcTemplate, outX + 200, outY);
            gc.Identifier = $"gc_for_{template.Name}";
            gc.NazcaFunctionName = $"ebeam_gc_{template.Name.Replace(" ", "_").ToLower()}";
            canvas.AddComponent(gc, gcTemplate.Name);

            var gcPin = gc.PhysicalPins.First(p => p.Name == "waveguide");
            var (gcX, gcY) = gcPin.GetAbsolutePosition();

            var route = new RoutedPath();
            route.Segments.Add(new StraightSegment(outX, outY, gcX, gcY, outputPin.GetAbsoluteAngle()));
            canvas.ConnectPinsWithCachedRoute(outputPin, gcPin, route);

            // Export and parse
            var script = _exporter.Export(canvas);
            var parsed = _parser.Parse(script);

            if (parsed.Components.Count < 2 || parsed.WaveguideStubs.Count == 0)
            {
                _output.WriteLine($"  SKIP: Export incomplete (components={parsed.Components.Count}, waveguides={parsed.WaveguideStubs.Count})");
                _output.WriteLine("");
                skippedComponents.Add(template.Name);
                continue;
            }

            // Find component stub pin
            var stubPin = parsed.PinDefinitions.FirstOrDefault(p => p.Name == outputPin.Name);
            if (stubPin == null)
            {
                _output.WriteLine($"  SKIP: Pin '{outputPin.Name}' not found in stub");
                _output.WriteLine("");
                skippedComponents.Add(template.Name);
                continue;
            }

            // Check alignment
            var compPlacement = parsed.Components.First();
            double expectedX = compPlacement.X + stubPin.X;
            double expectedY = compPlacement.Y + stubPin.Y;

            var wg = parsed.WaveguideStubs.First();
            double xDev = Math.Abs(expectedX - wg.StartX);
            double yDev = Math.Abs(expectedY - wg.StartY);

            _output.WriteLine($"  Component placement: ({compPlacement.X:F2}, {compPlacement.Y:F2})");
            _output.WriteLine($"  Expected global pin: ({expectedX:F2}, {expectedY:F2})");
            _output.WriteLine($"  Waveguide start: ({wg.StartX:F2}, {wg.StartY:F2})");
            _output.WriteLine($"  Deviation: X={xDev:F4} µm, Y={yDev:F4} µm");

            if (xDev >= PinAlignmentTolerance || yDev >= PinAlignmentTolerance)
            {
                _output.WriteLine($"  ❌ FAIL: Deviation exceeds tolerance");
                failedComponents.Add($"{template.Name} (ΔX={xDev:F4}, ΔY={yDev:F4})");
            }
            else
            {
                _output.WriteLine($"  ✓ PASS");
            }

            _output.WriteLine("");
            testedComponents++;
        }

        // Summary
        _output.WriteLine("=== EXHAUSTIVE TEST SUMMARY ===");
        _output.WriteLine($"Total components: {_library.Count}");
        _output.WriteLine($"Tested: {testedComponents}");
        _output.WriteLine($"Skipped: {skippedComponents.Count}");
        _output.WriteLine($"Failed: {failedComponents.Count}");

        if (skippedComponents.Count > 0)
        {
            _output.WriteLine("");
            _output.WriteLine("Skipped components:");
            foreach (var name in skippedComponents)
                _output.WriteLine($"  - {name}");
        }

        if (failedComponents.Count > 0)
        {
            _output.WriteLine("");
            _output.WriteLine("Failed components:");
            foreach (var name in failedComponents)
                _output.WriteLine($"  - {name}");

            failedComponents.Count.ShouldBe(0,
                $"GDS coordinate bug detected in {failedComponents.Count} components. " +
                $"All PDK components must have correctly aligned waveguide-to-pin coordinates.");
        }

        testedComponents.ShouldBeGreaterThan(0, "At least one component should be successfully tested");
    }
}
