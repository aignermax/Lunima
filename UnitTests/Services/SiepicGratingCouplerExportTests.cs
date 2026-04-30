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
///
/// Expected values are derived from the loaded PDK draft instead of being
/// hardcoded — the JSON is the source of truth, and these tests validate the
/// EXPORT MATH (rotation, Y-flip, origin offset application), not the JSON
/// content itself. That way a re-calibration that updates Width/Height/origin
/// in the JSON doesn't break the export tests.
/// </summary>
public class SiepicGratingCouplerExportTests
{
    private static string GetSiepicPdkPath() =>
        Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..",
            "CAP-DataAccess", "PDKs", "siepic-ebeam-pdk.json");

    private record GcCalibration(
        double Width, double Height,
        double OriginX, double OriginY,
        string FirstPinName, double FirstPinX, double FirstPinY);

    /// <summary>Load and unwrap the GC TE 1550 calibration from the bundled JSON.</summary>
    private static GcCalibration? LoadGcCalibration()
    {
        var pdkPath = GetSiepicPdkPath();
        if (!File.Exists(pdkPath)) return null;
        var pdk = new PdkLoader().LoadFromFile(pdkPath);
        var draft = pdk.Components.First(c => c.Name == "Grating Coupler TE 1550");
        var first = draft.Pins[0];
        return new GcCalibration(
            draft.WidthMicrometers, draft.HeightMicrometers,
            draft.NazcaOriginOffsetX ?? first.OffsetXMicrometers,
            draft.NazcaOriginOffsetY ?? first.OffsetYMicrometers,
            first.Name, first.OffsetXMicrometers, first.OffsetYMicrometers);
    }

    private static string ExportAt(GcCalibration cal, double x, double y, double rotationDegrees)
    {
        var pdk = new PdkLoader().LoadFromFile(GetSiepicPdkPath());
        var draft = pdk.Components.First(c => c.Name == "Grating Coupler TE 1550");
        var template = ConvertPdkComponentToTemplate(draft, pdk.Name, pdk.NazcaModuleName);
        var component = ComponentTemplates.CreateFromTemplate(template, x, y);
        component.RotationDegrees = rotationDegrees;
        var canvas = new DesignCanvasViewModel();
        canvas.AddComponent(component, "GC_TE1550");
        return new SimpleNazcaExporter().Export(canvas);
    }

    /// <summary>Apply the same rotation/Y-flip as the exporter to derive expected coords.</summary>
    private static (double X, double Y) ExpectedNazcaPosition(
        GcCalibration cal, double x, double y, double rotationDegrees)
    {
        var rad = rotationDegrees * Math.PI / 180.0;
        var rotX = cal.OriginX * Math.Cos(rad) - cal.OriginY * Math.Sin(rad);
        var rotY = cal.OriginX * Math.Sin(rad) + cal.OriginY * Math.Cos(rad);
        return (x + rotX, -(y + rotY));
    }

    private static SMatrix CreatePassThroughSMatrix(List<Pin> pins)
    {
        var pinIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        return new SMatrix(pinIds, new List<(Guid, double)>());
    }

    private static ComponentTemplate ConvertPdkComponentToTemplate(
        PdkComponentDraft pdkComp, string pdkName = "PDK", string? nazcaModuleName = null)
    {
        var pinDefs = pdkComp.Pins.Select(p => new PinDefinition(
            p.Name, p.OffsetXMicrometers, p.OffsetYMicrometers, p.AngleDegrees)).ToArray();
        var firstPin = pdkComp.Pins.FirstOrDefault();
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
            NazcaOriginOffsetX = pdkComp.NazcaOriginOffsetX ?? firstPin?.OffsetXMicrometers ?? 0,
            NazcaOriginOffsetY = pdkComp.NazcaOriginOffsetY ?? firstPin?.OffsetYMicrometers ?? 0,
            CreateSMatrix = CreatePassThroughSMatrix
        };
    }

    [Fact]
    public void GratingCouplerTE1550_AtOrigin_ExportsWithCorrectOffset()
    {
        var cal = LoadGcCalibration();
        if (cal is null) return;

        var result = ExportAt(cal, 0, 0, 0);
        var (nx, ny) = ExpectedNazcaPosition(cal, 0, 0, 0);

        var ci = CultureInfo.InvariantCulture;
        // Anchor on 'org' explicitly so .put() places the cell origin at the
        // computed (x, y) — Nazca's default anchor is the cell's first pin
        // (a0), which silently shifts the cell when a0 isn't at (0, 0).
        result.ShouldContain($"ebeam_gc_te1550().put('org', {nx.ToString("F2", ci)}, {ny.ToString("F2", ci)}, 0)",
            customMessage: "Component at origin should land at (originX, -originY) in Nazca coords, anchored on 'org'");
    }

    [Fact]
    public void GratingCouplerTE1550_AtNonZeroPosition_ExportsWithCorrectCoordinates()
    {
        var cal = LoadGcCalibration();
        if (cal is null) return;

        var result = ExportAt(cal, 100, 200, 0);
        var (nx, ny) = ExpectedNazcaPosition(cal, 100, 200, 0);

        var ci = CultureInfo.InvariantCulture;
        result.ShouldContain($"ebeam_gc_te1550().put('org', {nx.ToString("F2", ci)}, {ny.ToString("F2", ci)}, 0)");
    }

    [Theory]
    [InlineData(0,   0,   0)]
    [InlineData(100, 50,  0)]
    [InlineData(0,   0,   90)]
    [InlineData(0,   0,   180)]
    [InlineData(0,   0,   270)]
    public void GratingCouplerTE1550_VariousPositionsAndRotations_ExportsCorrectly(
        double x, double y, double rotationDegrees)
    {
        var cal = LoadGcCalibration();
        if (cal is null) return;

        var result = ExportAt(cal, x, y, rotationDegrees);
        var (nx, ny) = ExpectedNazcaPosition(cal, x, y, rotationDegrees);

        // The export rotation is the negation of the editor rotation (Y-axis
        // flip), normalized to 0/-90/-180/-270 (or 90 for the symmetric case).
        var ci = CultureInfo.InvariantCulture;
        var xPattern = nx.ToString("F2", ci);
        var yPattern = ny.ToString("F2", ci);
        var pattern = $@"ebeam_gc_te1550\(\)\.put\('org',\s*{Regex.Escape(xPattern)},\s*{Regex.Escape(yPattern)},";
        Regex.IsMatch(result, pattern).ShouldBeTrue(
            customMessage: $"Expected Nazca coords ({xPattern}, {yPattern}) for component at " +
                           $"({x}, {y}) with {rotationDegrees}° rotation.\nActual:\n{result}");
    }

    [Fact]
    public void GratingCouplerTE1550_Rotated90Degrees_ExportsWithRotatedOffset()
    {
        var cal = LoadGcCalibration();
        if (cal is null) return;
        var result = ExportAt(cal, 0, 0, 90);
        var (nx, ny) = ExpectedNazcaPosition(cal, 0, 0, 90);
        var ci = CultureInfo.InvariantCulture;
        result.ShouldMatch(
            $@"ebeam_gc_te1550\(\)\.put\('org',\s*{Regex.Escape(nx.ToString("F2", ci))},\s*{Regex.Escape(ny.ToString("F2", ci))},\s*-90\)");
    }

    [Fact]
    public void GratingCouplerTE1550_Rotated180Degrees_ExportsWithRotatedOffset()
    {
        var cal = LoadGcCalibration();
        if (cal is null) return;
        var result = ExportAt(cal, 0, 0, 180);
        var (nx, ny) = ExpectedNazcaPosition(cal, 0, 0, 180);
        var ci = CultureInfo.InvariantCulture;
        result.ShouldMatch(
            $@"ebeam_gc_te1550\(\)\.put\('org',\s*{Regex.Escape(nx.ToString("F2", ci))},\s*{Regex.Escape(ny.ToString("F2", ci))},\s*-?180\)");
    }

    [Fact]
    public void GratingCouplerTE1550_Rotated270Degrees_ExportsWithRotatedOffset()
    {
        var cal = LoadGcCalibration();
        if (cal is null) return;
        var result = ExportAt(cal, 0, 0, 270);
        var (nx, ny) = ExpectedNazcaPosition(cal, 0, 0, 270);
        var ci = CultureInfo.InvariantCulture;
        result.ShouldMatch(
            $@"ebeam_gc_te1550\(\)\.put\('org',\s*{Regex.Escape(nx.ToString("F2", ci))},\s*{Regex.Escape(ny.ToString("F2", ci))},\s*(-270|90)\)");
    }

    [Fact]
    public void GratingCouplerTE1550_StubGeneration_HasCorrectPinPositions()
    {
        var cal = LoadGcCalibration();
        if (cal is null) return;

        var result = ExportAt(cal, 0, 0, 0);

        var ci = CultureInfo.InvariantCulture;
        var pinX = (cal.FirstPinX - cal.OriginX).ToString("F2", ci);
        var pinY = ((cal.Height - cal.FirstPinY) - cal.OriginY).ToString("F2", ci);
        result.ShouldContain($"nd.Pin('{cal.FirstPinName}').put({pinX}, {pinY},");
    }
}
