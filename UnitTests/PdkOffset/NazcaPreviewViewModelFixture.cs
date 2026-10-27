using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.PdkOffset;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;

namespace UnitTests.PdkOffset;

/// <summary>Small synthetic draft and selected-preview fixtures for offset editor tests.</summary>
internal static class NazcaPreviewViewModelFixture
{
    internal static PdkOffsetEditorViewModel BuildVmWithSelection(
        PdkComponentDraft draft, NazcaPreviewResult cachedResult)
    {
        var vm = new PdkOffsetEditorViewModel(new CAP_DataAccess.Components.ComponentDraftMapper.BundledPdkCatalog(AppContext.BaseDirectory),
            new PdkLoader(), new PdkJsonSaver(),
            new CAP.Avalonia.ViewModels.Library.PdkManagerViewModel(),
            previewService: null);
        vm.Components.Add(new PdkComponentOffsetItemViewModel(draft, "auto-calib-test"));
        vm.SelectedComponent = vm.Components[0];
        vm.SeedNazcaResultForTesting(cachedResult);
        return vm;
    }

    internal static PdkComponentDraft BuildDraft() => new()
    {
        Name = "Coupler",
        NazcaFunction = "pdk.coupler",
        WidthMicrometers = 40,
        HeightMicrometers = 20,
        NazcaOriginOffsetX = 5.0,
        NazcaOriginOffsetY = 10.0,
        Pins = new List<PhysicalPinDraft>
        {
            new() { Name = "a0", OffsetXMicrometers = 0, OffsetYMicrometers = 5 },
        }
    };
}
