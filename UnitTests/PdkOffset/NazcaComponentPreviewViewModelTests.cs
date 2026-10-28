using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.PdkOffset;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using static UnitTests.PdkOffset.NazcaPreviewViewModelFixture;

namespace UnitTests.PdkOffset;

/// <summary>Offset editor dimensions, persistence, and absent-overlay behavior.</summary>
[Collection("NazcaComponentPreviewService")]
public class NazcaComponentPreviewViewModelTests
{
    // Keep localized status assertions independent of the developer or CI language.
    public NazcaComponentPreviewViewModelTests() =>
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

    [Fact]
    public void ViewModel_WithPreviewServiceNull_NazcaOverlayNotShown()
    {
        var vm = new PdkOffsetEditorViewModel(new CAP_DataAccess.Components.ComponentDraftMapper.BundledPdkCatalog(AppContext.BaseDirectory), new PdkLoader(), new PdkJsonSaver(), new PdkManagerViewModel(), previewService: null);
        var draft = BuildDraft();
        vm.Components.Add(new PdkComponentOffsetItemViewModel(draft, "Test"));
        vm.SelectedComponent = vm.Components[0];

        vm.HasNazcaOverlay.ShouldBeFalse();
        vm.NazcaPolygons.ShouldBeEmpty();
        vm.NazcaPinStubs.ShouldBeEmpty();
    }

    [Fact]
    public void ViewModel_WhenComponentSelected_SetsComponentDimensions()
    {
        var vm = new PdkOffsetEditorViewModel(new CAP_DataAccess.Components.ComponentDraftMapper.BundledPdkCatalog(AppContext.BaseDirectory), new PdkLoader(), new PdkJsonSaver(), new PdkManagerViewModel(), previewService: null);
        var draft = BuildDraft();
        vm.Components.Add(new PdkComponentOffsetItemViewModel(draft, "Test"));
        vm.SelectedComponent = vm.Components[0];

        vm.ComponentWidth.ShouldBe(40.0);
        vm.ComponentHeight.ShouldBe(20.0);
    }

    [Fact]
    public void ViewModel_ApplyOffset_SavesWidthAndHeightToDraft()
    {
        var vm = new PdkOffsetEditorViewModel(new CAP_DataAccess.Components.ComponentDraftMapper.BundledPdkCatalog(AppContext.BaseDirectory), new PdkLoader(), new PdkJsonSaver(), new PdkManagerViewModel(), previewService: null);
        var draft = BuildDraft();
        vm.Components.Add(new PdkComponentOffsetItemViewModel(draft, "Test"));
        vm.SelectedComponent = vm.Components[0];

        vm.ComponentWidth = 55.0;
        vm.ComponentHeight = 25.0;
        vm.ApplyOffsetCommand.Execute(null);

        vm.SelectedComponent.Draft.WidthMicrometers.ShouldBe(55.0);
        vm.SelectedComponent.Draft.HeightMicrometers.ShouldBe(25.0);
    }

    [Fact]
    public void ViewModel_CanvasComponentLeft_IsCanvasPaddingWhenNoOverlay()
    {
        var vm = new PdkOffsetEditorViewModel(new CAP_DataAccess.Components.ComponentDraftMapper.BundledPdkCatalog(AppContext.BaseDirectory), new PdkLoader(), new PdkJsonSaver(), new PdkManagerViewModel(), previewService: null);
        var draft = BuildDraft();
        vm.Components.Add(new PdkComponentOffsetItemViewModel(draft, "Test"));
        vm.SelectedComponent = vm.Components[0];

        // Without overlay, CanvasComponentLeft should equal CanvasPadding (20)
        vm.CanvasComponentLeft.ShouldBe(20.0);
        vm.CanvasComponentTop.ShouldBe(20.0);
    }
}
