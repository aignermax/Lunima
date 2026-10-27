using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.PdkOffset;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using static UnitTests.PdkOffset.NazcaPreviewViewModelFixture;

namespace UnitTests.PdkOffset;

/// <summary>Pin delta calculations and bipartite correspondence.</summary>
[Collection("NazcaComponentPreviewService")]
public class NazcaComponentPreviewPinAlignmentTests
{
    // Keep localized status assertions independent of the developer or CI language.
    public NazcaComponentPreviewPinAlignmentTests() =>
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

    [Fact]
    public void ComputePinAlignment_PinAtNazcaOrigin_ReportsZeroDistance()
    {
        // Lunima pin sits at OffsetX=5, OffsetY=3 from the bbox top-left in y-down.
        // NazcaOriginOffset=(5,3) — the offset is the org measured from the bbox
        // TOP-LEFT corner (mapper convention), so the Nazca origin is at (5, 3)
        // in the same y-down system → exactly where the pin is.
        // In Nazca-space (y-up) the pin should land at (0, 0).
        var draft = new PdkComponentDraft
        {
            WidthMicrometers = 20,
            HeightMicrometers = 10,
            NazcaOriginOffsetX = 5,
            NazcaOriginOffsetY = 3,
            Pins = new List<PhysicalPinDraft>
            {
                new() { Name = "in", OffsetXMicrometers = 5, OffsetYMicrometers = 3 }
            }
        };
        var result = new NazcaPreviewResult
        {
            Success = true,
            Pins = new List<NazcaPreviewPin>
            {
                new() { Name = "opt1", X = 0, Y = 0 }  // sits at the Nazca origin
            }
        };

        var vm = new PdkOffsetEditorViewModel(new CAP_DataAccess.Components.ComponentDraftMapper.BundledPdkCatalog(AppContext.BaseDirectory),
            new PdkLoader(), new PdkJsonSaver(),
            new CAP.Avalonia.ViewModels.Library.PdkManagerViewModel(),
            previewService: null);
        vm.ComputePinAlignment(result, draft);

        vm.PinAlignmentResults.Count.ShouldBe(1);
        var info = vm.PinAlignmentResults[0];
        info.DistanceMicrometers.ShouldBe(0.0, tolerance: 0.001);
        info.IsAligned.ShouldBeTrue();
    }

    [Fact]
    public void ComputePinAlignment_PinFiveMicrometresAway_ReportsExactDelta()
    {
        // Lunima pin at top-left (0,0); NazcaOrigin at the centre of a 10×10
        // bbox: (5, 5). In y-down distance-from-origin: (-5, -5) but we flip
        // Y so Nazca-space coords become (-5, +5). A Nazca pin at (0,0) is
        // therefore 5√2 ≈ 7.07 µm away.
        var draft = new PdkComponentDraft
        {
            WidthMicrometers = 10,
            HeightMicrometers = 10,
            NazcaOriginOffsetX = 5,
            NazcaOriginOffsetY = 5,
            Pins = new List<PhysicalPinDraft>
            {
                new() { Name = "edge", OffsetXMicrometers = 0, OffsetYMicrometers = 0 }
            }
        };
        var result = new NazcaPreviewResult
        {
            Success = true,
            Pins = new List<NazcaPreviewPin>
            {
                new() { Name = "centre", X = 0, Y = 0 }
            }
        };

        var vm = new PdkOffsetEditorViewModel(new CAP_DataAccess.Components.ComponentDraftMapper.BundledPdkCatalog(AppContext.BaseDirectory),
            new PdkLoader(), new PdkJsonSaver(),
            new CAP.Avalonia.ViewModels.Library.PdkManagerViewModel(),
            previewService: null);
        vm.ComputePinAlignment(result, draft);

        var info = vm.PinAlignmentResults[0];
        info.DeltaXMicrometers.ShouldBe(5.0, tolerance: 0.001);
        info.DeltaYMicrometers.ShouldBe(-5.0, tolerance: 0.001);
        info.DistanceMicrometers.ShouldBe(Math.Sqrt(50), tolerance: 0.001);
        info.IsAligned.ShouldBeFalse();
    }

    [Fact]
    public void ComputePinAlignment_BipartiteMatchesEvaluate()
    {
        // Reproduces ultrareview bug 1: two Lunima pins clustered near one
        // Nazca pin used to collapse onto it (independent NN), masking that
        // the second Nazca pin had no Lunima counterpart. The inline
        // overlay would say "all aligned" while Check-All said "Misaligned".
        var draft = new PdkComponentDraft
        {
            Name = "twin", NazcaFunction = "x",
            WidthMicrometers = 12, HeightMicrometers = 1,
            // Calibrated per mapper contract: oy = YMax (1), so a draft pin at
            // OffsetY 0 projects to Nazca y = oy - 0 = 1 (the pins' row).
            NazcaOriginOffsetX = 0, NazcaOriginOffsetY = 1,
            Pins = new List<PhysicalPinDraft>
            {
                new() { Name = "in",  OffsetXMicrometers = 0,    OffsetYMicrometers = 0 },
                new() { Name = "in2", OffsetXMicrometers = 0.4,  OffsetYMicrometers = 0 },
            }
        };
        var result = new NazcaPreviewResult
        {
            Success = true,
            XMin = 0, YMin = 0, XMax = 12, YMax = 1,
            Pins = new List<NazcaPreviewPin>
            {
                new() { Name = "n1", X = 0,  Y = 1, Angle = 0 },
                new() { Name = "n2", X = 12, Y = 1, Angle = 0 },
            }
        };

        var vm = BuildVmWithSelection(draft, result);
        vm.ComputePinAlignment(result, draft);

        // The forced second pair (in2 ↔ n2) is 11.6 µm away — the inline
        // verdict must reflect that, not silently collapse 'in2' onto n1.
        vm.PinAlignmentResults.Count.ShouldBe(2);
        vm.PinAlignmentResults.ShouldContain(p =>
            p.LunimaPinName == "in2" && p.DistanceMicrometers > 10);
        vm.PinAlignmentSummary.ShouldContain("1/2");
    }
}
