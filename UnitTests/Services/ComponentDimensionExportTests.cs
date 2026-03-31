using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Shouldly;
using Xunit;

namespace UnitTests.Services;

/// <summary>
/// Tests that component dimensions in Nazca stub generation match the component's
/// WidthMicrometers and HeightMicrometers properties exactly.
/// Reproduces issue #69: MMI 2x2 appears shorter in GDS export.
/// </summary>
public class ComponentDimensionExportTests
{
    [Fact]
    public void Export_Mmi2x2FromDemoPdk_UsesDirectDemoCall()
    {
        // Demo PDK components loaded from demo-pdk.json use nazcaFunction "demo.mmi2x2_dp".
        // These are called directly via nazca.demofab (import nazca.demofab as demo), not via stubs.
        var pdkPath = FindPdkFile("demo-pdk.json");
        if (pdkPath == null) return; // skip if PDK not found

        var loader = new PdkLoader();
        var pdk = loader.LoadFromFile(pdkPath);

        // Component is named "2x2 MMI Coupler" with nazcaFunction "demo.mmi2x2_dp"
        var mmi2x2Draft = pdk.Components.First(c => c.Name == "2x2 MMI Coupler");
        mmi2x2Draft.ShouldNotBeNull();

        // Create component from template
        var canvas = new DesignCanvasViewModel();
        var template = ConvertPdkComponentToTemplate(mmi2x2Draft, pdk.Name, pdk.NazcaModuleName);
        var component = ComponentTemplates.CreateFromTemplate(template, 0, 0);
        canvas.AddComponent(component, template.Name);

        // Act - Export to Nazca
        var exporter = new SimpleNazcaExporter();
        var result = exporter.Export(canvas);

        // Assert - Should use direct demo call (demo.mmi2x2_dp is a real nazca.demofab function)
        result.ShouldContain("demo.mmi2x2_dp()");
        result.ShouldContain("import nazca.demofab as demo");
    }

    [Fact]
    public void Export_RotatedComponent_StubStillUsesOriginalDimensions()
    {
        // A rotated component's stub should still use the un-rotated dimensions
        // (rotation happens at placement time, not in the cell definition)
        var canvas = new DesignCanvasViewModel();

        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "ebeam_test_device",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: "Test",
            rotationCounterClock: DiscreteRotation.R0,
            physicalPins: new List<PhysicalPin>
            {
                new PhysicalPin
                {
                    Name = "p1",
                    OffsetXMicrometers = 0,
                    OffsetYMicrometers = 5,
                    AngleDegrees = 180
                }
            }
        );

        component.WidthMicrometers = 100;
        component.HeightMicrometers = 50;
        component.RotationDegrees = 90; // Rotate 90 degrees
        component.PhysicalX = 0;
        component.PhysicalY = 0;

        canvas.AddComponent(component, "Test");

        var exporter = new SimpleNazcaExporter();
        var result = exporter.Export(canvas);

        // Stub should use original dimensions (100x50), not swapped
        result.ShouldContain("Auto-generated stub for ebeam_test_device (100x50 µm)");
        result.ShouldContain("nd.Polygon(points=[(0,0),(100.00,0),(100.00,50.00),(0,50.00)], layer=1)");

        // Component placement should include rotation angle (pin offset may vary)
        result.ShouldContain(", -90)  #"); // Rotation angle should be -90 (Y-axis flipped)
    }

    [Theory]
    [InlineData(120, 50)] // Demo PDK MMI 2x2
    [InlineData(100, 40)] // Demo PDK MMI 1x2
    [InlineData(200, 20)] // Demo PDK Phase Shifter
    public void Export_VariousDimensions_StubMatchesComponentProperties(double width, double height)
    {
        var canvas = new DesignCanvasViewModel();

        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "ebeam_component",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: "TestComp",
            rotationCounterClock: DiscreteRotation.R0,
            physicalPins: new List<PhysicalPin>()
        );

        component.WidthMicrometers = width;
        component.HeightMicrometers = height;
        component.PhysicalX = 0;
        component.PhysicalY = 0;

        canvas.AddComponent(component, "TestComp");

        var exporter = new SimpleNazcaExporter();
        var result = exporter.Export(canvas);

        // Verify dimensions in comment
        result.ShouldContain($"Auto-generated stub for ebeam_component ({width}x{height} µm)");

        // Verify polygon coordinates
        var w = width.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        var h = height.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        result.ShouldContain($"nd.Polygon(points=[(0,0),({w},0),({w},{h}),(0,{h})], layer=1)");
    }

    [Fact]
    public void Export_ComponentWithPhysicalPins_PinPositionsCorrect()
    {
        // Verify that pin positions in the stub match the PhysicalPin offsets
        var canvas = new DesignCanvasViewModel();

        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "ebeam_mmi_test",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: "TestMMI",
            rotationCounterClock: DiscreteRotation.R0,
            physicalPins: new List<PhysicalPin>
            {
                new PhysicalPin
                {
                    Name = "a0",
                    OffsetXMicrometers = 0,
                    OffsetYMicrometers = 12.5,
                    AngleDegrees = 180
                },
                new PhysicalPin
                {
                    Name = "a1",
                    OffsetXMicrometers = 0,
                    OffsetYMicrometers = 37.5,
                    AngleDegrees = 180
                },
                new PhysicalPin
                {
                    Name = "b0",
                    OffsetXMicrometers = 120,
                    OffsetYMicrometers = 12.5,
                    AngleDegrees = 0
                },
                new PhysicalPin
                {
                    Name = "b1",
                    OffsetXMicrometers = 120,
                    OffsetYMicrometers = 37.5,
                    AngleDegrees = 0
                }
            }
        );

        component.WidthMicrometers = 120;
        component.HeightMicrometers = 50;
        component.PhysicalX = 0;
        component.PhysicalY = 0;

        canvas.AddComponent(component, "TestMMI");

        var exporter = new SimpleNazcaExporter();
        var result = exporter.Export(canvas);

        // Pins use Nazca Y-up coordinate system: nazca_y = height - editor_y
        // a0: (0, 50 - 12.5 = 37.5), angle 180 -> -180
        result.ShouldContain("nd.Pin('a0').put(0.00, 37.50, -180)");

        // a1: (0, 50 - 37.5 = 12.5), angle 180 -> -180
        result.ShouldContain("nd.Pin('a1').put(0.00, 12.50, -180)");

        // b0: (120, 50 - 12.5 = 37.5), angle 0 -> 0
        result.ShouldContain("nd.Pin('b0').put(120.00, 37.50, 0)");

        // b1: (120, 50 - 37.5 = 12.5), angle 0 -> 0
        result.ShouldContain("nd.Pin('b1').put(120.00, 12.50, 0)");
    }

    private static ComponentTemplate ConvertPdkComponentToTemplate(
        CAP_DataAccess.Components.ComponentDraftMapper.DTOs.PdkComponentDraft pdkComp,
        string pdkName,
        string? nazcaModuleName)
    {
        var pinDefs = pdkComp.Pins.Select(p => new PinDefinition(
            p.Name, p.OffsetXMicrometers, p.OffsetYMicrometers, p.AngleDegrees
        )).ToArray();

        return new ComponentTemplate
        {
            Name = pdkComp.Name,
            Category = pdkComp.Category,
            WidthMicrometers = pdkComp.WidthMicrometers,
            HeightMicrometers = pdkComp.HeightMicrometers,
            PinDefinitions = pinDefs,
            NazcaFunctionName = pdkComp.NazcaFunction,
            NazcaParameters = pdkComp.NazcaParameters,
            PdkSource = pdkName,
            NazcaModuleName = nazcaModuleName,
            CreateSMatrix = pins =>
            {
                var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
                return new SMatrix(pinIds, new());
            }
        };
    }

    private static string? FindPdkFile(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PDKs", fileName),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "CAP-DataAccess", "PDKs", fileName),
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
