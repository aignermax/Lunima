using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.PdkOffset;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using static UnitTests.PdkOffset.NazcaPreviewViewModelFixture;

namespace UnitTests.PdkOffset;

/// <summary>Calibration angle propagation and Y-axis convention conversion.</summary>
[Collection("NazcaComponentPreviewService")]
public class NazcaComponentPreviewAngleTests
{
    // Keep localized status assertions independent of the developer or CI language.
    public NazcaComponentPreviewAngleTests() =>
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

    [Fact]
    public void AutoCalibrate_PropagatesNazcaAngleToLunimaPin()
    {
        // The Nazca pin angle is ground truth — leaving the Lunima
        // AngleDegrees on a stale value let us ship pin records where the
        // angle implied a different bbox edge than the position.
        // 180° is invariant under the Y-flip convention so this case
        // exercises the propagation path without the convention mapping.
        var draft = new PdkComponentDraft
        {
            Name = "rot", NazcaFunction = "x", WidthMicrometers = 10, HeightMicrometers = 10,
            Pins = new List<PhysicalPinDraft>
            {
                new() { Name = "p", OffsetXMicrometers = 0, OffsetYMicrometers = 5, AngleDegrees = 999 }
            }
        };
        var result = new NazcaPreviewResult
        {
            Success = true, XMin = 0, YMin = 0, XMax = 10, YMax = 10,
            Pins = new List<NazcaPreviewPin> { new() { X = 0, Y = 5, Angle = 180 } }
        };

        PdkOffsetCalibration.ApplyAutoCalibrate(draft, result);
        draft.Pins[0].AngleDegrees.ShouldBe(180);
    }

    [Theory]
    [InlineData(0,   0)]    // invariant
    [InlineData(180, 180)]  // invariant
    [InlineData(90,  270)]  // Y-flip: Nazca up → Lunima up (in Y-down screen, 270 = stub up)
    [InlineData(270, 90)]   // Y-flip: Nazca down → Lunima down (in Y-down screen, 90 = stub down)
    [InlineData(360, 0)]    // wrap
    [InlineData(45,  315)]  // diagonals also flip
    public void AutoCalibrate_AppliesYFlipConventionToWrittenAngle(double nazcaAngle, double expectedLunima)
    {
        // Without the Y-flip, a Nazca pin angle of 90° landed in Lunima JSON as 90°
        // and rendered as a downward stub on Avalonia's Y-down canvas — pointing
        // into the component body instead of up out of it. The reverse for 270°.
        // PR #539's GDS overlay made this visible; this test pins the conversion
        // so future calibrations can't silently regress.
        var draft = new PdkComponentDraft
        {
            Name = "vp", NazcaFunction = "x", WidthMicrometers = 10, HeightMicrometers = 10,
            Pins = new List<PhysicalPinDraft>
            {
                new() { Name = "p", OffsetXMicrometers = 5, OffsetYMicrometers = 5, AngleDegrees = 0 }
            }
        };
        var result = new NazcaPreviewResult
        {
            Success = true, XMin = 0, YMin = 0, XMax = 10, YMax = 10,
            Pins = new List<NazcaPreviewPin> { new() { X = 5, Y = 5, Angle = nazcaAngle } }
        };

        PdkOffsetCalibration.ApplyAutoCalibrate(draft, result);
        draft.Pins[0].AngleDegrees.ShouldBe(expectedLunima);
    }

    [Theory]
    [InlineData(0,    0)]
    [InlineData(90,   270)]
    [InlineData(180,  180)]
    [InlineData(270,  90)]
    [InlineData(360,  0)]
    [InlineData(-90,  90)]   // negative input
    [InlineData(450,  270)]  // > 360 input
    public void FlipAngleConvention_IsSelfInverseAndNormalisesInput(double input, double expected)
    {
        var flipped = PdkOffsetCalibration.FlipAngleConvention(input);
        flipped.ShouldBe(expected, tolerance: 1e-9);
        // Selbstinvers: applying twice returns the normalised input
        var roundTrip = PdkOffsetCalibration.FlipAngleConvention(flipped);
        roundTrip.ShouldBe(((input % 360) + 360) % 360, tolerance: 1e-9);
    }

    [Fact]
    public void AngleDisagreement_OppositeAngles_CostsFullDiagonal()
    {
        // 180° apart pins on a 10×10 bbox should incur a 10√2 µm penalty —
        // larger than any same-edge position tie, so the matcher refuses
        // the cross-pair.
        var penalty = PdkOffsetCalibration.AngleDisagreementMicrometers(0, 180, Math.Sqrt(200));
        penalty.ShouldBe(Math.Sqrt(200), tolerance: 0.001);
    }

    [Fact]
    public void AngleDisagreement_SameAngle_ReturnsZero()
    {
        PdkOffsetCalibration.AngleDisagreementMicrometers(90, 90, 50)
            .ShouldBe(0, tolerance: 0.001);
        // 360 ≡ 0 so a wrap-around comparison must also be free.
        PdkOffsetCalibration.AngleDisagreementMicrometers(0, 360, 50)
            .ShouldBe(0, tolerance: 0.001);
    }
}
