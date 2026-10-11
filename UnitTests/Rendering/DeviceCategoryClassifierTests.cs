using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.Controls.Rendering.DeviceCategories;
using Shouldly;
using Xunit;

namespace UnitTests.Rendering;

/// <summary>
/// Tests for <see cref="DeviceCategoryClassifier"/>: the shipped PDK names fold into the right
/// visual category, the specific rules win over the generic ones, and the category palette never
/// reuses the selection accent or the danger color.
/// </summary>
public class DeviceCategoryClassifierTests
{
    [Theory]
    [InlineData("Straight Waveguide 100µm", "demo.shallow.strt", DeviceCategory.Waveguide)]
    [InlineData("90° Bend", "demo.shallow.bend", DeviceCategory.Waveguide)]
    [InlineData("2x2 MMI Coupler", "demo.mmi2x2_dp", DeviceCategory.Coupler)]
    [InlineData("Y-Branch 1550", "ebeam_y_1550", DeviceCategory.Coupler)]
    [InlineData("Phase Shifter", "demo.eopm_dc", DeviceCategory.Active)]
    [InlineData("Grating Coupler TE 1550", "ebeam_gc_te1550", DeviceCategory.InputOutput)]
    [InlineData("Edge Coupler", "demo.io", DeviceCategory.InputOutput)]
    [InlineData("Photodetector", "demo.pd", DeviceCategory.Detector)]
    [InlineData("Disconnected Waveguide TE 1550", "ebeam_terminator_te1550", DeviceCategory.Detector)]
    [InlineData("Contra-Directional Coupler", "contra_directional_coupler", DeviceCategory.Filter)]
    [InlineData("MZI", null, DeviceCategory.Filter)]
    [InlineData("Waveguide Crossing", "demo_crossing", DeviceCategory.Crossing)]
    [InlineData("Bond Pad", "ebeam_BondPad", DeviceCategory.Electrical)]
    [InlineData("ONA Analyzer", "__analyzer__", DeviceCategory.Detector)]
    public void ShippedPdkNames_FoldIntoTheirVisualCategory(string name, string? nazcaFunction, DeviceCategory expected)
    {
        DeviceCategoryClassifier.ClassifyNames(name, name, nazcaFunction, hasElectricalPin: false).ShouldBe(expected);
    }

    [Fact]
    public void UnknownName_WithElectricalPin_IsActive_OtherwiseOther()
    {
        DeviceCategoryClassifier.ClassifyNames("Widget", "Widget", "cell_x", hasElectricalPin: true).ShouldBe(DeviceCategory.Active);
        DeviceCategoryClassifier.ClassifyNames("Widget", "Widget", "cell_x", hasElectricalPin: false).ShouldBe(DeviceCategory.Other);
    }

    [Fact]
    public void CategoryAccents_NeverReuseTheSelectionOrDangerColor()
    {
        foreach (var category in Enum.GetValues<DeviceCategory>())
        {
            var accent = DeviceCategoryPalette.AccentColor(category);
            accent.ShouldNotBe(CanvasPalette.Accent, category.ToString());
            accent.ShouldNotBe(CanvasPalette.Danger, category.ToString());
        }
    }
}
