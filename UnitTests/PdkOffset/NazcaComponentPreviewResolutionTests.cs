using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.PdkOffset;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;

namespace UnitTests.PdkOffset;

/// <summary>PDK module and function resolution for nested and flat names.</summary>
[Collection("NazcaComponentPreviewService")]
public class NazcaComponentPreviewResolutionTests
{
    // Keep localized status assertions independent of the developer or CI language.
    public NazcaComponentPreviewResolutionTests() =>
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

    [Fact]
    public void ResolveModuleAndFunction_DottedDemoNotation_SplitsAtLastDot()
    {
        var (module, function) = PdkOffsetEditorViewModel.ResolveModuleAndFunction("demo.mmi2x2_dp");
        module.ShouldBe("demo");
        function.ShouldBe("mmi2x2_dp");
    }

    [Fact]
    public void ResolveModuleAndFunction_NestedDottedNotation_SplitsAtLastDotOnly()
    {
        // Real PDKs sometimes use deeper paths like "siepic_ebeam_pdk.mmi.dc_1550".
        // Only the last segment is the function name.
        var (module, function) = PdkOffsetEditorViewModel.ResolveModuleAndFunction("siepic_ebeam_pdk.mmi.dc_1550");
        module.ShouldBe("siepic_ebeam_pdk.mmi");
        function.ShouldBe("dc_1550");
    }

    [Theory]
    [InlineData("ebeam_y_1550")]
    [InlineData("ebeam_dc_te1550")]
    [InlineData("gc_te1550_8deg_oxide_1")]
    [InlineData("GC_TE_1550_8degOxide_BB")]      // uppercase GC_ — SiEPIC ships these too
    [InlineData("GC_SiN_TE_1310_8degOxide_BB")]  // and the SiN variants
    [InlineData("EBEAM_y_branch")]               // case-insensitive ebeam_ (defensive)
    public void ResolveModuleAndFunction_SiepicPrefixedFlatName_RoutesToSiepicEbeamPdk(string fn)
    {
        var (module, function) = PdkOffsetEditorViewModel.ResolveModuleAndFunction(fn);
        module.ShouldBe("siepic_ebeam_pdk");
        function.ShouldBe(fn);
    }

    [Fact]
    public void ResolveModuleAndFunction_UnrecognisedFlatName_FallsBackToDemo()
    {
        var (module, function) = PdkOffsetEditorViewModel.ResolveModuleAndFunction("custom_thing");
        module.ShouldBe("demo");
        function.ShouldBe("custom_thing");
    }

    [Fact]
    public void ResolveModuleAndFunction_NullOrEmpty_FallsBackToDemo()
    {
        var (m1, f1) = PdkOffsetEditorViewModel.ResolveModuleAndFunction(null);
        m1.ShouldBe("demo"); f1.ShouldBe("");

        var (m2, f2) = PdkOffsetEditorViewModel.ResolveModuleAndFunction("");
        m2.ShouldBe("demo"); f2.ShouldBe("");
    }
}
