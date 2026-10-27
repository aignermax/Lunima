using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.PdkOffset;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using static UnitTests.PdkOffset.NazcaPreviewViewModelFixture;

namespace UnitTests.PdkOffset;

/// <summary>Automatic bbox calibration, alignment status, and command preconditions.</summary>
[Collection("NazcaComponentPreviewService")]
public class NazcaComponentPreviewCalibrationTests
{
    // Keep localized status assertions independent of the developer or CI language.
    public NazcaComponentPreviewCalibrationTests() =>
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

    [Fact]
    public void AutoCalibrate_AppliesNazcaBboxToWidthHeightAndOrigin()
    {
        // Nazca bbox: x ∈ [-2, 8], y ∈ [-3, 7]. Origin (0,0) sits 2 µm from
        // the left edge and 7 µm below the TOP edge → NazcaOriginOffset=(2,7)
        // per the NazcaCoordinateMapper contract (ox = -XMin, oy = YMax).
        var draft = new PdkComponentDraft
        {
            Name = "test_comp", NazcaFunction = "pdk.test",
            WidthMicrometers = 1, HeightMicrometers = 1,  // intentionally wrong
            NazcaOriginOffsetX = 0, NazcaOriginOffsetY = 0,
            Pins = new List<PhysicalPinDraft>
            {
                new() { Name = "in", OffsetXMicrometers = 0, OffsetYMicrometers = 0 }
            }
        };
        var result = new NazcaPreviewResult
        {
            Success = true,
            XMin = -2, YMin = -3, XMax = 8, YMax = 7,
            Pins = new List<NazcaPreviewPin>
            {
                new() { Name = "opt1", X = 5, Y = 4 }
            }
        };

        var vm = BuildVmWithSelection(draft, result);
        vm.AutoCalibrateCommand.Execute(null);

        draft.WidthMicrometers.ShouldBe(10.0, tolerance: 0.001);
        draft.HeightMicrometers.ShouldBe(10.0, tolerance: 0.001);
        draft.NazcaOriginOffsetX!.Value.ShouldBe(2.0, tolerance: 0.001);
        draft.NazcaOriginOffsetY!.Value.ShouldBe(7.0, tolerance: 0.001);
        // The single Lunima pin gets snapped to the Nazca pin position:
        //   OffsetX = nazcaX - XMin = 5 - (-2) = 7
        //   OffsetY = YMax - nazcaY = 7 - 4 = 3
        draft.Pins[0].OffsetXMicrometers.ShouldBe(7.0, tolerance: 0.001);
        draft.Pins[0].OffsetYMicrometers.ShouldBe(3.0, tolerance: 0.001);
        vm.HasUnsavedChanges.ShouldBeTrue();
    }

    [Fact]
    public void AutoCalibrate_AfterApplying_AlignmentSummaryReportsAllAligned()
    {
        // Calibrating must leave every pin within tolerance of its matched
        // Nazca pin — that's the whole point of the command.
        var draft = new PdkComponentDraft
        {
            Name = "two_port", NazcaFunction = "pdk.two_port",
            WidthMicrometers = 1, HeightMicrometers = 1,
            Pins = new List<PhysicalPinDraft>
            {
                new() { Name = "in",  OffsetXMicrometers = 0, OffsetYMicrometers = 0 },
                new() { Name = "out", OffsetXMicrometers = 1, OffsetYMicrometers = 1 }
            }
        };
        var result = new NazcaPreviewResult
        {
            Success = true,
            XMin = 0, YMin = 0, XMax = 12, YMax = 6,
            Pins = new List<NazcaPreviewPin>
            {
                new() { Name = "a0", X = 0,  Y = 3 },
                new() { Name = "b0", X = 12, Y = 3 }
            }
        };

        var vm = BuildVmWithSelection(draft, result);
        vm.AutoCalibrateCommand.Execute(null);

        vm.PinAlignmentResults.Count.ShouldBe(2);
        vm.PinAlignmentResults.ShouldAllBe(p => p.IsAligned);
        vm.PinAlignmentSummary.ShouldContain("All 2/2");
    }

    [Fact]
    public void AutoCalibrate_PinCountMismatch_AbortsAndLeavesDraftUnchanged()
    {
        // The Lunima JSON for SiEPIC's GC sometimes declares only one pin
        // even though the cell exposes 'io' + 'wg'. Auto-calibrate must
        // refuse rather than silently drop or duplicate a pin.
        var draft = new PdkComponentDraft
        {
            Name = "single_pin", NazcaFunction = "pdk.single",
            WidthMicrometers = 5, HeightMicrometers = 5,
            NazcaOriginOffsetX = 0, NazcaOriginOffsetY = 0,
            Pins = new List<PhysicalPinDraft>
            {
                new() { Name = "io", OffsetXMicrometers = 0, OffsetYMicrometers = 2.5 }
            }
        };
        var result = new NazcaPreviewResult
        {
            Success = true,
            XMin = 0, YMin = 0, XMax = 30, YMax = 10,
            Pins = new List<NazcaPreviewPin>
            {
                new() { Name = "io", X = 0,  Y = 5 },
                new() { Name = "wg", X = 30, Y = 5 }
            }
        };

        var vm = BuildVmWithSelection(draft, result);
        vm.AutoCalibrateCommand.Execute(null);

        // Bbox + pins must stay at their pre-calibration values
        draft.WidthMicrometers.ShouldBe(5.0);
        draft.HeightMicrometers.ShouldBe(5.0);
        draft.Pins[0].OffsetXMicrometers.ShouldBe(0);
        draft.Pins[0].OffsetYMicrometers.ShouldBe(2.5);
        vm.HasUnsavedChanges.ShouldBeFalse();
        vm.StatusText.ShouldContain("pin counts must match");
    }

    [Fact]
    public void AutoCalibrate_WithoutCachedPreview_CannotExecute()
    {
        // The button is disabled until a successful render lands; programmatic
        // Execute must short-circuit with a status message instead of crashing.
        var draft = new PdkComponentDraft
        {
            Name = "no_render", NazcaFunction = "pdk.none",
            WidthMicrometers = 1, HeightMicrometers = 1,
            Pins = new List<PhysicalPinDraft>()
        };
        var vm = new PdkOffsetEditorViewModel(new CAP_DataAccess.Components.ComponentDraftMapper.BundledPdkCatalog(AppContext.BaseDirectory),
            new PdkLoader(), new PdkJsonSaver(),
            new CAP.Avalonia.ViewModels.Library.PdkManagerViewModel(),
            previewService: null);
        vm.Components.Add(new PdkComponentOffsetItemViewModel(draft, "no-render-test"));
        vm.SelectedComponent = vm.Components[0];

        vm.AutoCalibrateCommand.CanExecute(null).ShouldBeFalse();
    }
}
