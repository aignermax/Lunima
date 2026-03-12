using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace UnitTests.Services;

/// <summary>
/// Tests for Issue #66: Grating Coupler TE 1550 position offset in GDS export.
/// Verifies that the Grating Coupler TE 1550 from SiEPIC EBeam PDK exports
/// to Nazca Python with correct positioning and rotation handling.
/// </summary>
public class SiepicGratingCouplerExportTests
{
    private static string GetSiepicPdkPath() =>
        Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..",
            "CAP-DataAccess", "PDKs", "siepic-ebeam-pdk.json");

    /// <summary>
    /// Creates a simple pass-through S-matrix for testing (no functional simulation needed).
    /// </summary>
    private static SMatrix CreatePassThroughSMatrix(List<Pin> pins)
    {
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        return new SMatrix(pinIds, new List<(Guid, double)>());
    }

    /// <summary>
    /// Helper to create a ComponentTemplate from a PDK component (mirrors MainViewModel logic).
    /// </summary>
    private static ComponentTemplate ConvertPdkComponentToTemplate(
        PdkComponentDraft pdkComp,
        string pdkName = "PDK",
        string? nazcaModuleName = null)
    {
        var pinDefs = pdkComp.Pins.Select(p => new PinDefinition(
            p.Name,
            p.OffsetXMicrometers,
            p.OffsetYMicrometers,
            p.AngleDegrees
        )).ToArray();

        // Calculate Nazca origin offset from first pin position
        var firstPin = pdkComp.Pins.FirstOrDefault();
        double nazcaOriginOffsetX = firstPin?.OffsetXMicrometers ?? 0;
        double nazcaOriginOffsetY = firstPin?.OffsetYMicrometers ?? 0;

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
            NazcaOriginOffsetX = nazcaOriginOffsetX,
            NazcaOriginOffsetY = nazcaOriginOffsetY,
            CreateSMatrix = CreatePassThroughSMatrix
        };
    }

    [Fact]
    public void GratingCouplerTE1550_AtOrigin_ExportsWithCorrectOffset()
    {
        // Arrange - Load the actual Grating Coupler TE 1550 from SiEPIC PDK
        var pdkPath = GetSiepicPdkPath();
        if (!File.Exists(pdkPath))
        {
            // Skip test if PDK file not found (e.g., in CI environment)
            return;
        }

        var loader = new PdkLoader();
        var pdk = loader.LoadFromFile(pdkPath);
        var gratingCouplerDraft = pdk.Components.First(c => c.Name == "Grating Coupler TE 1550");

        // Verify PDK data is as expected
        gratingCouplerDraft.WidthMicrometers.ShouldBe(30);
        gratingCouplerDraft.HeightMicrometers.ShouldBe(30);
        gratingCouplerDraft.NazcaFunction.ShouldBe("ebeam_gc_te1550");

        // First pin should be at (15, 30)
        var firstPin = gratingCouplerDraft.Pins[0];
        firstPin.OffsetXMicrometers.ShouldBe(15);
        firstPin.OffsetYMicrometers.ShouldBe(30);

        // Convert to template and create component
        var template = ConvertPdkComponentToTemplate(gratingCouplerDraft, pdk.Name, pdk.NazcaModuleName);
        template.NazcaOriginOffsetX.ShouldBe(15, "Nazca origin offset X should match first pin X");
        template.NazcaOriginOffsetY.ShouldBe(30, "Nazca origin offset Y should match first pin Y");

        var component = ComponentTemplates.CreateFromTemplate(template, 0, 0);
        component.NazcaOriginOffsetX.ShouldBe(15);
        component.NazcaOriginOffsetY.ShouldBe(30);

        // Add to canvas and export
        var canvas = new DesignCanvasViewModel();
        canvas.AddComponent(component, "GC_TE1550");

        var exporter = new SimpleNazcaExporter();
        var result = exporter.Export(canvas);

        // Assert - Component should be placed with offset applied
        // Physical position (0, 0) + origin offset (15, 30) with Y-flip = (15, -30)
        result.ShouldContain("ebeam_gc_te1550().put(15.00, -30.00, 0)",
            customMessage: "Component placement should account for NazcaOriginOffset");

        // Verify stub definition includes correct pins
        result.ShouldContain("def ebeam_gc_te1550(**kwargs):");
        result.ShouldContain("nd.Pin('port 1')");
        result.ShouldContain("nd.Pin('port 2')");
    }

    [Fact]
    public void GratingCouplerTE1550_AtNonZeroPosition_ExportsWithCorrectCoordinates()
    {
        // Arrange
        var pdkPath = GetSiepicPdkPath();
        if (!File.Exists(pdkPath)) return;

        var loader = new PdkLoader();
        var pdk = loader.LoadFromFile(pdkPath);
        var gratingCouplerDraft = pdk.Components.First(c => c.Name == "Grating Coupler TE 1550");

        var template = ConvertPdkComponentToTemplate(gratingCouplerDraft, pdk.Name, pdk.NazcaModuleName);
        var component = ComponentTemplates.CreateFromTemplate(template, 100, 200);

        var canvas = new DesignCanvasViewModel();
        canvas.AddComponent(component, "GC_TE1550");

        // Act
        var exporter = new SimpleNazcaExporter();
        var result = exporter.Export(canvas);

        // Assert - Position (100, 200) + origin offset (15, 30) with Y-flip
        // nazcaX = 100 + 15 = 115
        // nazcaY = -(200 + 30) = -230
        result.ShouldContain("ebeam_gc_te1550().put(115.00, -230.00, 0)",
            customMessage: "Component at (100, 200) with offset (15, 30) should be at Nazca coords (115, -230)");
    }

    [Fact]
    public void GratingCouplerTE1550_Rotated90Degrees_ExportsWithRotatedOffset()
    {
        // Arrange
        var pdkPath = GetSiepicPdkPath();
        if (!File.Exists(pdkPath)) return;

        var loader = new PdkLoader();
        var pdk = loader.LoadFromFile(pdkPath);
        var gratingCouplerDraft = pdk.Components.First(c => c.Name == "Grating Coupler TE 1550");

        var template = ConvertPdkComponentToTemplate(gratingCouplerDraft, pdk.Name, pdk.NazcaModuleName);
        var component = ComponentTemplates.CreateFromTemplate(template, 0, 0);

        // Rotate 90 degrees counter-clockwise
        component.RotationDegrees = 90;

        var canvas = new DesignCanvasViewModel();
        canvas.AddComponent(component, "GC_TE1550");

        // Act
        var exporter = new SimpleNazcaExporter();
        var result = exporter.Export(canvas);

        // Assert - Rotation transform:
        // Original offset: (15, 30)
        // Rotated 90° CCW: offsetX' = 15*cos(90°) - 30*sin(90°) = 0 - 30 = -30
        //                  offsetY' = 15*sin(90°) + 30*cos(90°) = 15 + 0 = 15
        // Position (0, 0) + rotated offset (-30, 15) with Y-flip = (-30, -15)
        // Nazca rotation: -90° (Y-axis flip)
        result.ShouldMatch(@"ebeam_gc_te1550\(\)\.put\(-30\.00,\s*-15\.00,\s*-90\)",
            customMessage: "90° rotation should transform offset from (15, 30) to (-30, 15) before placement");
    }

    [Fact]
    public void GratingCouplerTE1550_Rotated180Degrees_ExportsWithRotatedOffset()
    {
        // Arrange
        var pdkPath = GetSiepicPdkPath();
        if (!File.Exists(pdkPath)) return;

        var loader = new PdkLoader();
        var pdk = loader.LoadFromFile(pdkPath);
        var gratingCouplerDraft = pdk.Components.First(c => c.Name == "Grating Coupler TE 1550");

        var template = ConvertPdkComponentToTemplate(gratingCouplerDraft, pdk.Name, pdk.NazcaModuleName);
        var component = ComponentTemplates.CreateFromTemplate(template, 0, 0);

        // Rotate 180 degrees
        component.RotationDegrees = 180;

        var canvas = new DesignCanvasViewModel();
        canvas.AddComponent(component, "GC_TE1550");

        // Act
        var exporter = new SimpleNazcaExporter();
        var result = exporter.Export(canvas);

        // Assert - Rotation transform:
        // Original offset: (15, 30)
        // Rotated 180°: offsetX' = 15*cos(180°) - 30*sin(180°) = -15 - 0 = -15
        //               offsetY' = 15*sin(180°) + 30*cos(180°) = 0 + (-30) = -30
        // Position (0, 0) + rotated offset (-15, -30) with Y-flip = (-15, 30)
        // Nazca rotation: -180° (Y-axis flip)
        result.ShouldMatch(@"ebeam_gc_te1550\(\)\.put\(-15\.00,\s*30\.00,\s*-?180\)",
            customMessage: "180° rotation should transform offset from (15, 30) to (-15, -30) before placement");
    }

    [Fact]
    public void GratingCouplerTE1550_Rotated270Degrees_ExportsWithRotatedOffset()
    {
        // Arrange
        var pdkPath = GetSiepicPdkPath();
        if (!File.Exists(pdkPath)) return;

        var loader = new PdkLoader();
        var pdk = loader.LoadFromFile(pdkPath);
        var gratingCouplerDraft = pdk.Components.First(c => c.Name == "Grating Coupler TE 1550");

        var template = ConvertPdkComponentToTemplate(gratingCouplerDraft, pdk.Name, pdk.NazcaModuleName);
        var component = ComponentTemplates.CreateFromTemplate(template, 0, 0);

        // Rotate 270 degrees counter-clockwise
        component.RotationDegrees = 270;

        var canvas = new DesignCanvasViewModel();
        canvas.AddComponent(component, "GC_TE1550");

        // Act
        var exporter = new SimpleNazcaExporter();
        var result = exporter.Export(canvas);

        // Assert - Rotation transform:
        // Original offset: (15, 30)
        // Rotated 270° CCW: offsetX' = 15*cos(270°) - 30*sin(270°) = 0 - 30*(-1) = 30
        //                   offsetY' = 15*sin(270°) + 30*cos(270°) = 15*(-1) + 0 = -15
        // Position (0, 0) + rotated offset (30, -15) with Y-flip = (30, 15)
        // Nazca rotation: -270° = 90° (Y-axis flip, normalized)
        result.ShouldMatch(@"ebeam_gc_te1550\(\)\.put\(30\.00,\s*15\.00,\s*(-270|90)\)",
            customMessage: "270° rotation should transform offset from (15, 30) to (30, -15) before placement");
    }

    [Theory]
    [InlineData(0, 0, 0, 15.00, -30.00)]      // No rotation, origin offset (15, 30)
    [InlineData(100, 50, 0, 115.00, -80.00)]   // Position offset with no rotation
    [InlineData(0, 0, 90, -30.00, -15.00)]     // 90° rotation transforms (15, 30) → (-30, 15)
    [InlineData(0, 0, 180, -15.00, 30.00)]     // 180° rotation transforms (15, 30) → (-15, -30)
    [InlineData(0, 0, 270, 30.00, 15.00)]      // 270° rotation transforms (15, 30) → (30, -15)
    public void GratingCouplerTE1550_VariousPositionsAndRotations_ExportsCorrectly(
        double x, double y, double rotationDegrees,
        double expectedNazcaX, double expectedNazcaY)
    {
        // Arrange
        var pdkPath = GetSiepicPdkPath();
        if (!File.Exists(pdkPath)) return;

        var loader = new PdkLoader();
        var pdk = loader.LoadFromFile(pdkPath);
        var gratingCouplerDraft = pdk.Components.First(c => c.Name == "Grating Coupler TE 1550");

        var template = ConvertPdkComponentToTemplate(gratingCouplerDraft, pdk.Name, pdk.NazcaModuleName);
        var component = ComponentTemplates.CreateFromTemplate(template, x, y);
        component.RotationDegrees = rotationDegrees;

        var canvas = new DesignCanvasViewModel();
        canvas.AddComponent(component, "GC_TE1550");

        // Act
        var exporter = new SimpleNazcaExporter();
        var result = exporter.Export(canvas);

        // Assert - Build regex pattern to match the expected coordinates
        var ci = CultureInfo.InvariantCulture;
        var xPattern = expectedNazcaX.ToString("F2", ci);
        var yPattern = expectedNazcaY.ToString("F2", ci);
        var pattern = $@"ebeam_gc_te1550\(\)\.put\({Regex.Escape(xPattern)},\s*{Regex.Escape(yPattern)},";

        Regex.IsMatch(result, pattern).ShouldBeTrue(
            customMessage: $"Expected Nazca coords ({expectedNazcaX.ToString("F2", ci)}, {expectedNazcaY.ToString("F2", ci)}) for " +
                          $"component at ({x}, {y}) with {rotationDegrees}° rotation.\nActual export:\n{result}");
    }

    [Fact]
    public void GratingCouplerTE1550_StubGeneration_HasCorrectPinPositions()
    {
        // Arrange
        var pdkPath = GetSiepicPdkPath();
        if (!File.Exists(pdkPath)) return;

        var loader = new PdkLoader();
        var pdk = loader.LoadFromFile(pdkPath);
        var gratingCouplerDraft = pdk.Components.First(c => c.Name == "Grating Coupler TE 1550");

        var template = ConvertPdkComponentToTemplate(gratingCouplerDraft, pdk.Name, pdk.NazcaModuleName);
        var component = ComponentTemplates.CreateFromTemplate(template, 0, 0);

        var canvas = new DesignCanvasViewModel();
        canvas.AddComponent(component, "GC_TE1550");

        // Act
        var exporter = new SimpleNazcaExporter();
        var result = exporter.Export(canvas);

        // Assert - Check stub definition has correct pin positions
        // Port 1: (15, 30) in Avalonia → (15, 30-30=0) in Nazca, angle 90° → -90° with Y-flip
        result.ShouldContain("nd.Pin('port 1').put(15.00, 0.00, -90)");

        // Port 2: (30, 15) in Avalonia → (30, 30-15=15) in Nazca, angle 0° → 0° with Y-flip
        result.ShouldContain("nd.Pin('port 2').put(30.00, 15.00, 0)");
    }
}
