using CAP.Avalonia.Controls.Rendering;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Controls.Rendering;

/// <summary>
/// Tests for <see cref="PinPitchSizer.ComputeScale"/>: closely spaced ports (e.g. the 2x2 MMI
/// with 8 µm port pitch) must shrink the pin glyph so neighbouring markers no longer merge
/// into one blob, while widely spaced pins keep their full size.
/// </summary>
public class PinPitchSizerTests
{
    private const double BaseRadius = 5.0;

    private static PhysicalPin Pin(double x, double y) =>
        new() { Name = "p", OffsetXMicrometers = x, OffsetYMicrometers = y };

    [Fact]
    public void SinglePin_KeepsFullSize()
    {
        PinPitchSizer.ComputeScale(new[] { Pin(0, 0) }, BaseRadius).ShouldBe(1.0);
    }

    [Fact]
    public void WidelySpacedPins_KeepFullSize()
    {
        var pins = new[] { Pin(0, 0), Pin(0, 100), Pin(250, 0), Pin(250, 100) };
        PinPitchSizer.ComputeScale(pins, BaseRadius).ShouldBe(1.0);
    }

    [Fact]
    public void MmiLikePitch_ShrinksBelowHalfPitch()
    {
        // 2x2 MMI: two ports per side, 8 µm apart.
        var pins = new[] { Pin(0, 26), Pin(0, 34), Pin(250, 26), Pin(250, 34) };

        double scale = PinPitchSizer.ComputeScale(pins, BaseRadius);

        double effectiveRadius = BaseRadius * scale;
        effectiveRadius.ShouldBeLessThan(4.0); // markers no longer touch
        effectiveRadius.ShouldBe(8.0 * PinPitchSizer.MaxRadiusToPitchFraction, 1e-9);
    }

    [Fact]
    public void LargerBaseRadius_ShrinksMore_SoEffectiveRadiusMatchesPitch()
    {
        var pins = new[] { Pin(0, 0), Pin(0, 8) };

        double effective = 8.0 * PinPitchSizer.ComputeScale(pins, 8.0);

        effective.ShouldBe(8.0 * PinPitchSizer.MaxRadiusToPitchFraction, 1e-9);
    }

    [Fact]
    public void CoincidentPins_AreFlooredAtMinScale()
    {
        var pins = new[] { Pin(0, 0), Pin(0, 0) };
        PinPitchSizer.ComputeScale(pins, BaseRadius).ShouldBe(PinPitchSizer.MinScale);
    }

    [Fact]
    public void NonPositiveBaseRadius_KeepsFullSize()
    {
        var pins = new[] { Pin(0, 0), Pin(0, 8) };
        PinPitchSizer.ComputeScale(pins, 0).ShouldBe(1.0);
    }
}
