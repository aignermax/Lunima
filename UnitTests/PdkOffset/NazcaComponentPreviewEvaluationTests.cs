using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.PdkOffset;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using static UnitTests.PdkOffset.NazcaPreviewViewModelFixture;

namespace UnitTests.PdkOffset;

/// <summary>Preview calibration status and failure outcomes.</summary>
[Collection("NazcaComponentPreviewService")]
public class NazcaComponentPreviewEvaluationTests
{
    // Keep localized status assertions independent of the developer or CI language.
    public NazcaComponentPreviewEvaluationTests() =>
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

    [Fact]
    public void Evaluate_RenderFailed_ReturnsRenderFailedStatus()
    {
        var draft = new PdkComponentDraft { Name = "x", Pins = new() };
        var result = NazcaPreviewResult.Fail("module not found");

        var check = PdkOffsetCalibration.Evaluate(draft, result, 0.5);
        check.Status.ShouldBe(ComponentCheckStatus.RenderFailed);
        check.Message.ShouldContain("module not found");
        check.IsAutoFixable.ShouldBeFalse();
    }

    [Fact]
    public void Evaluate_PinCountMismatch_NotAutoFixable()
    {
        var draft = new PdkComponentDraft
        {
            Name = "gc", WidthMicrometers = 5, HeightMicrometers = 5,
            Pins = new() { new() { Name = "io", OffsetXMicrometers = 0, OffsetYMicrometers = 2.5 } }
        };
        var result = new NazcaPreviewResult
        {
            Success = true, XMin = 0, YMin = 0, XMax = 30, YMax = 10,
            Pins = new List<NazcaPreviewPin>
            {
                new() { X = 0, Y = 5 }, new() { X = 30, Y = 5 }
            }
        };

        var check = PdkOffsetCalibration.Evaluate(draft, result, 0.5);
        check.Status.ShouldBe(ComponentCheckStatus.PinCountMismatch);
        check.IsAutoFixable.ShouldBeFalse();
        check.LunimaPinCount.ShouldBe(1);
        check.NazcaPinCount.ShouldBe(2);
    }

    [Fact]
    public void Evaluate_AlignedComponent_ReportsAlignedAndZeroDelta()
    {
        // Lunima draft is already perfectly calibrated to the Nazca data
        var draft = new PdkComponentDraft
        {
            Name = "perfect", WidthMicrometers = 10, HeightMicrometers = 10,
            // oy = YMax (10); pin at OffsetY 10 projects to y = 10 - 10 = 0.
            NazcaOriginOffsetX = 0, NazcaOriginOffsetY = 10,
            Pins = new() { new() { Name = "in", OffsetXMicrometers = 0, OffsetYMicrometers = 10 } }
        };
        var result = new NazcaPreviewResult
        {
            Success = true, XMin = 0, YMin = 0, XMax = 10, YMax = 10,
            Pins = new List<NazcaPreviewPin> { new() { X = 0, Y = 0 } }
        };

        var check = PdkOffsetCalibration.Evaluate(draft, result, 0.5);
        check.Status.ShouldBe(ComponentCheckStatus.Aligned);
        check.WorstDeltaMicrometers.ShouldBe(0.0, tolerance: 0.001);
    }

    [Fact]
    public void Evaluate_MisalignedComponent_FlaggedAsAutoFixable()
    {
        // Pin is 3 µm off in X — far above the 0.5 µm tolerance, but counts
        // match so Auto-Calibrate would resolve it.
        var draft = new PdkComponentDraft
        {
            Name = "shifted", WidthMicrometers = 10, HeightMicrometers = 10,
            // oy = YMax (10) keeps Y aligned; the ox = 3 shift is the 3 µm error.
            NazcaOriginOffsetX = 3, NazcaOriginOffsetY = 10,
            Pins = new() { new() { Name = "in", OffsetXMicrometers = 0, OffsetYMicrometers = 10 } }
        };
        var result = new NazcaPreviewResult
        {
            Success = true, XMin = 0, YMin = 0, XMax = 10, YMax = 10,
            Pins = new List<NazcaPreviewPin> { new() { X = 0, Y = 0 } }
        };

        var check = PdkOffsetCalibration.Evaluate(draft, result, 0.5);
        check.Status.ShouldBe(ComponentCheckStatus.Misaligned);
        check.IsAutoFixable.ShouldBeTrue();
        check.WorstDeltaMicrometers.ShouldBe(3.0, tolerance: 0.001);
    }

    [Fact]
    public void Evaluate_NoNazcaPins_ReturnsNoNazcaPins()
    {
        var draft = new PdkComponentDraft
        {
            Name = "blackbox",
            Pins = new() { new() { Name = "in" } }
        };
        var result = new NazcaPreviewResult { Success = true, Pins = Array.Empty<NazcaPreviewPin>() };

        var check = PdkOffsetCalibration.Evaluate(draft, result, 0.5);
        check.Status.ShouldBe(ComponentCheckStatus.NoNazcaPins);
        check.IsAutoFixable.ShouldBeFalse();
    }

    [Fact]
    public void ApplyAutoCalibrate_DegenerateBbox_ReturnsDegenerateOutcome()
    {
        var draft = new PdkComponentDraft
        {
            Name = "x", WidthMicrometers = 1, HeightMicrometers = 1,
            Pins = new() { new() { Name = "p" } }
        };
        var result = new NazcaPreviewResult
        {
            Success = true, XMin = 5, YMin = 5, XMax = 5, YMax = 5,  // zero area
            Pins = new List<NazcaPreviewPin> { new() { X = 5, Y = 5 } }
        };

        PdkOffsetCalibration.ApplyAutoCalibrate(draft, result)
            .ShouldBe(AutoCalibrateOutcome.DegenerateBbox);
        // Draft must be unchanged — degeneracy detected before mutation
        draft.WidthMicrometers.ShouldBe(1.0);
    }

    [Fact]
    public void ApplyAutoCalibrate_FailedRender_ReturnsNoPreviewWithoutCrashing()
    {
        var draft = new PdkComponentDraft { Name = "x", Pins = new() };
        var result = NazcaPreviewResult.Fail("anything");

        PdkOffsetCalibration.ApplyAutoCalibrate(draft, result)
            .ShouldBe(AutoCalibrateOutcome.NoPreview);
    }
}
