using CAP.Avalonia.ViewModels.ComponentSettings.InstanceOverride;
using CAP_Core.Export;
using CAP_DataAccess.Persistence.PIR;
using Shouldly;
using Xunit;

namespace UnitTests.ComponentSettings.InstanceOverride;

/// <summary>
/// Tests for <see cref="OverridePinMapper"/> coordinate conversion and pin-name matching (issue #561).
/// </summary>
public class OverridePinMapperTests
{
    private static NazcaPreviewResult OkResultWithPins(
        double w = 20, double h = 10,
        IReadOnlyList<NazcaPreviewPin>? pins = null) => new()
    {
        Success = true,
        XMin = 0, YMin = 0, XMax = w, YMax = h,
        Pins = pins ?? Array.Empty<NazcaPreviewPin>(),
    };

    // ─── BuildOverridePins coordinate conversion ─────────────────────────────

    [Fact]
    public void BuildOverridePins_ConvertsNazcaCoordsToComponentLocal()
    {
        // Bounding box: X 0..20, Y 0..10.  Pin at (5, 3), angle 180.
        var result = OkResultWithPins(w: 20, h: 10, pins: new[]
        {
            new NazcaPreviewPin { Name = "a0", X = 5, Y = 3, Angle = 180 },
        });

        var pins = OverridePinMapper.BuildOverridePins(result);

        pins.Count.ShouldBe(1);
        pins[0].Name.ShouldBe("a0");
        pins[0].OffsetXMicrometers.ShouldBe(5 - 0, tolerance: 0.001);   // X − XMin
        pins[0].OffsetYMicrometers.ShouldBe(10 - 3, tolerance: 0.001);  // YMax − Y
        pins[0].AngleDegrees.ShouldBe(-180, tolerance: 0.001);           // negated
    }

    [Fact]
    public void BuildOverridePins_NonZeroBboxOffset_SubtractsXMin()
    {
        // BBox shifted: XMin=5, YMin=2.
        var result = new NazcaPreviewResult
        {
            Success = true,
            XMin = 5, XMax = 25, YMin = 2, YMax = 12,
            Pins = new[] { new NazcaPreviewPin { Name = "b0", X = 10, Y = 7, Angle = 0 } },
        };

        var pins = OverridePinMapper.BuildOverridePins(result);

        pins[0].OffsetXMicrometers.ShouldBe(10 - 5, tolerance: 0.001);  // = 5
        pins[0].OffsetYMicrometers.ShouldBe(12 - 7, tolerance: 0.001);  // = 5
        pins[0].AngleDegrees.ShouldBe(0, tolerance: 0.001);             // −0 = 0
    }

    // ─── PinNamesMatch ────────────────────────────────────────────────────────

    [Fact]
    public void PinNamesMatch_SameNames_ReturnsTrue()
    {
        var a = new[] { new OverridePinData { Name = "a0" }, new OverridePinData { Name = "b0" } };
        var b = new[] { new OverridePinData { Name = "b0" }, new OverridePinData { Name = "a0" } };
        OverridePinMapper.PinNamesMatch(a, b).ShouldBeTrue();
    }

    [Fact]
    public void PinNamesMatch_DifferentNames_ReturnsFalse()
    {
        var a = new[] { new OverridePinData { Name = "a0" } };
        var b = new[] { new OverridePinData { Name = "c0" } };
        OverridePinMapper.PinNamesMatch(a, b).ShouldBeFalse();
    }

    [Fact]
    public void PinNamesMatch_DifferentCounts_ReturnsFalse()
    {
        var a = new[] { new OverridePinData { Name = "a0" } };
        var b = new[] { new OverridePinData { Name = "a0" }, new OverridePinData { Name = "b0" } };
        OverridePinMapper.PinNamesMatch(a, b).ShouldBeFalse();
    }

    [Fact]
    public void PinNamesMatch_BothEmpty_ReturnsTrue()
        => OverridePinMapper.PinNamesMatch(
            Array.Empty<OverridePinData>(), Array.Empty<OverridePinData>()).ShouldBeTrue();
}
