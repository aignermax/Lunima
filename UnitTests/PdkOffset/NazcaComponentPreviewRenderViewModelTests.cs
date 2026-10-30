using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.PdkOffset;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using static UnitTests.PdkOffset.NazcaPreviewRenderingFixture;

namespace UnitTests.PdkOffset;

/// <summary>Optional real-render overlay flow and module-resolution diagnostics.</summary>
[Collection("NazcaComponentPreviewService")]
public class NazcaComponentPreviewRenderViewModelTests
{
    // Keep localized status assertions independent of the developer or CI language.
    public NazcaComponentPreviewRenderViewModelTests() =>
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

    [Fact]
    public async Task EndToEnd_ViewModel_DemoMmi2x2Dp_RendersOverlay()
    {
        // Reproduces the exact user click that produced
        //   "Preview unavailable: module 'nazca.demofab' has no attribute 'demo.mmi2x2_dp'"
        // before ResolveModuleAndFunction was added. Demo PDK is bundled with
        // Nazca, so this test is a strict assertion when Python+Nazca are
        // available — anything less than a populated overlay is a real bug.
        var vm = await TryRenderThroughViewModel("demo.mmi2x2_dp");
        if (vm == null) return;  // python or script missing — environment skip

        // Genuinely no Nazca? Status would say "No module named 'nazca'".
        if (vm.NazcaOverlayStatus.Contains("No module named 'nazca'", StringComparison.Ordinal))
            return;

        vm.IsNazcaRendering.ShouldBeFalse(
            $"Render never completed. Last status: '{vm.NazcaOverlayStatus}'");
        vm.HasNazcaOverlay.ShouldBeTrue(
            $"Demo MMI 2x2 must render through the full ViewModel pipeline. Status: {vm.NazcaOverlayStatus}");
        vm.NazcaPinStubs.Count.ShouldBeGreaterThanOrEqualTo(2,
            "a 2x2 MMI coupler must expose at least 2 pin stubs in the overlay");
    }

    /// <summary>
    /// Regression coverage for every NazcaFunction string the bundled PDK JSONs
    /// actually use. Each case below corresponds to one bug a manual click
    /// surfaced; together they keep the resolver, the dotted-path walk in
    /// _build_cell, the SiEPIC fixed-cell GDS reader, the SiEPIC PCell route
    /// and the multi-layer polygon extraction honest.
    /// </summary>
    [Theory]
    [InlineData("demo.shallow.strt", "Straight waveguide — nested demofab path (demo.shallow.<fn>)")]
    [InlineData("demo.shallow.bend", "90° bend — same nested-path resolver case")]
    [InlineData("demo.mmi2x2_dp",     "2x2 MMI — single-level demofab path")]
    [InlineData("ebeam_y_1550",       "SiEPIC fixed-cell GDS path")]
    [InlineData("ebeam_dc_te1550",    "SiEPIC PCell path (parametric DC)")]
    [InlineData("ebeam_gc_te1550",    "SiEPIC GC: polygons on layer 998/0, no PinRec — must still report polygons>0")]
    public async Task EndToEnd_ViewModel_RealPdkComponent_RendersWithoutAttributeError(string nazcaFunction, string description)
    {
        var vm = await TryRenderThroughViewModel(nazcaFunction);
        if (vm == null) return;  // python missing — environment skip
        if (IsEnvironmentSkip(vm.NazcaOverlayStatus)) return;

        // None of these legit Lunima PDK references should ever produce an
        // attribute lookup error — that means our routing dropped the dotted
        // path or hit the wrong module.
        vm.NazcaOverlayStatus.Contains("no attribute").ShouldBeFalse(
            $"[{description}] '{nazcaFunction}' resolved to the wrong module/attribute. " +
            $"Status: {vm.NazcaOverlayStatus}");

        // A render that did go through must populate at least the bbox-driven
        // overlay flag — geometry counts depend on whether gdstk is installed
        // and whether the component carries pin metadata, so we don't pin them.
        vm.HasNazcaOverlay.ShouldBeTrue(
            $"[{description}] render did not produce an overlay. Status: {vm.NazcaOverlayStatus}");
    }

    [Fact]
    public async Task EndToEnd_ViewModel_RingResonator_ReportsMissingDemofabAttributeClearly()
    {
        // demo_pdk.ring_resonator is referenced in the bundled demo PDK JSON
        // but nazca.demofab doesn't expose ring_resonator. Until the PDK
        // metadata is corrected the render must fail with an actionable
        // message — silently rendering nothing would hide the data bug.
        var vm = await TryRenderThroughViewModel("demo_pdk.ring_resonator");
        if (vm == null) return;
        if (vm.NazcaOverlayStatus.Contains("No module named", StringComparison.Ordinal))
            return;

        vm.HasNazcaOverlay.ShouldBeFalse();
        vm.NazcaOverlayStatus.Contains("ring_resonator").ShouldBeTrue(
            $"Expected error message to name the missing attribute. Status: {vm.NazcaOverlayStatus}");
    }

    [Fact]
    public async Task EndToEnd_ViewModel_SiepicEbeamYJunction_RoutesToSiepicEbeamPdkModule()
    {
        // SiEPIC EBeam PDK is a KLayout package, not a Nazca cell library:
        // siepic_ebeam_pdk does not expose `ebeam_y_1550` as a top-level
        // attribute. The realistic outcome is "module 'siepic_ebeam_pdk'
        // has no attribute …" — that means the routing in
        // ResolveModuleAndFunction worked (we hit the correct module) and
        // the limitation is upstream, not a Lunima bug. The negative
        // assertion below catches the *previous* bug, where the same
        // request hit nazca.demofab instead.
        var vm = await TryRenderThroughViewModel("ebeam_y_1550");
        if (vm == null) return;

        // The status must mention siepic_ebeam_pdk, NOT nazca.demofab — the
        // routing fix is exactly what this test guards.
        vm.NazcaOverlayStatus.Contains("nazca.demofab", StringComparison.Ordinal).ShouldBeFalse(
            $"Flat SiEPIC name was routed to nazca.demofab instead of siepic_ebeam_pdk. " +
            $"Status: {vm.NazcaOverlayStatus}");
    }
}
