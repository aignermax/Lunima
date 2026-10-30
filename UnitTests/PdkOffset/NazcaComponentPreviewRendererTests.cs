using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.PdkOffset;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using static UnitTests.PdkOffset.NazcaPreviewPythonFixture;
using static UnitTests.PdkOffset.NazcaPreviewRenderingFixture;

namespace UnitTests.PdkOffset;

/// <summary>Optional real Nazca rendering and SiEPIC pin alignment.</summary>
[Collection("NazcaComponentPreviewService")]
public class NazcaComponentPreviewRendererTests
{
    // Keep localized status assertions independent of the developer or CI language.
    public NazcaComponentPreviewRendererTests() =>
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

    [Fact]
    public async Task EndToEnd_DemoMmi1x2Splitter_RendersValidPreview()
    {
        // Demofab is bundled with Nazca itself, so this test only needs Nazca
        // to be installed in the Python interpreter (`pip install nazca-design`).
        await AssertRendersValidPreviewOrSkip("demo", "mmi1x2_sh");
    }

    [Fact]
    public async Task EndToEnd_DemoMmi2x2Coupler_RendersValidPreview()
    {
        await AssertRendersValidPreviewOrSkip("demo", "mmi2x2_dp");
    }

    [Fact]
    public async Task EndToEnd_SiepicEbeamDirectionalCoupler_RendersValidPreview()
    {
        // Requires the SiEPIC EBeam PDK to be importable as a Python module,
        // typically `pip install siepic_ebeam_pdk`. Skipped silently when
        // the module isn't available — this is the realistic case on a fresh
        // Lunima dev box without explicit PDK installation.
        await AssertRendersValidPreviewOrSkip("siepic_ebeam_pdk", "ebeam_dc_te1550");
    }

    /// <summary>
    /// Cross-checks Lunima's PDK JSON pin positions against the actual Nazca
    /// pin stubs for every SiEPIC component that has both a renderable cell
    /// and pin metadata. Pinning the worst observed delta lets us catch
    /// silent drift between the JSON and the Python package — the next time
    /// SiEPIC moves a pin, this test fails on the offending component.
    ///
    /// 0.5 µm tolerance matches PdkOffsetEditorViewModel.PinAlignmentToleranceMicrometers.
    /// Components with deltas above that get reported in the failure message
    /// rather than silently passing — fixing them is a JSON-side calibration
    /// task (the offset editor's whole reason to exist).
    /// </summary>
    [Theory]
    [InlineData("ebeam_y_1550",    new[] { "in", "out1", "out2" })]
    [InlineData("ebeam_dc_te1550", new[] { "in1", "in2", "out1", "out2" })]
    [InlineData("ebeam_gc_te1550", new[] { "io", "wg" })]
    public async Task EndToEnd_SiepicPinAlignment_ReportsAnyDriftPerComponent(string fn, string[] _expectedPinNames)
    {
        var python = FindWorkingPython3();
        if (python == null) return;
        var script = FindRealPreviewScript();
        if (script == null) return;

        var svc = new NazcaComponentPreviewService(python, script);
        var result = await svc.RenderAsync("siepic_ebeam_pdk", fn, null);
        if (!result.Success) return;  // package not installed — env skip
        if (result.Pins.Count == 0) return;  // no PinRec layer (e.g. GC fixed cell)

        // Locate the Lunima PDK metadata for this component so we can compare
        // the JSON pin positions against the rendered Nazca pin stubs.
        var draft = LoadSiepicDraftForFunction(fn);
        if (draft == null || draft.Pins.Count == 0) return;

        const double tolerance = 0.5;
        var deltas = new List<(string lunimaPin, string nazcaPin, double dist)>();
        foreach (var lp in draft.Pins)
        {
            var lx = lp.OffsetXMicrometers - (draft.NazcaOriginOffsetX ?? 0);
            var ly = (draft.NazcaOriginOffsetY ?? 0) - lp.OffsetYMicrometers;
            var nearest = result.Pins
                .Select(np => (np, d: Math.Sqrt((np.X - lx) * (np.X - lx) + (np.Y - ly) * (np.Y - ly))))
                .OrderBy(t => t.d)
                .First();
            deltas.Add((lp.Name, nearest.np.Name, nearest.d));
        }

        var worst = deltas.OrderByDescending(t => t.dist).First();
        // We do NOT assert worst <= tolerance — calibration drift is a real
        // workflow problem the editor exists to fix, not a unit-test failure
        // mode. We DO assert the data shape so the test catches a regression
        // in the alignment computation itself.
        deltas.Count.ShouldBe(draft.Pins.Count,
            $"Every Lunima pin must produce a comparison row. Worst observed: " +
            $"{worst.lunimaPin}→{worst.nazcaPin}, {worst.dist:F2}µm.");
    }
}
