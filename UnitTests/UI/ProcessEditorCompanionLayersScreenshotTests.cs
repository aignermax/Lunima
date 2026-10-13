using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.Views;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// The process editor shows the "Waveguide companion layers" card with the shipped demo
/// process' trench band (2/0, Trench, 8 µm each side). The window is rendered taller than its
/// default so the card is in view. Writes to <c>docs/pr-media/waveguide-companion-layers</c>
/// with <c>CAP_UPDATE_PR_MEDIA=1</c>.
/// </summary>
[Collection("LocalizationSingleton")]
public class ProcessEditorCompanionLayersScreenshotTests
{
    private const double WindowHeight = 1180;

    [AvaloniaFact]
    public void ProcessEditor_ShowsTheDemoTrenchBand()
    {
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
        var demo = new PdkLoader().LoadFromFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PDKs", "demo-pdk.json"));
        var vm = new ProcessManagementViewModel(Mock.Of<IFileDialogService>());
        vm.Load(demo.Process!);
        vm.CompanionLayers.ShouldHaveSingleItem().Name.ShouldBe("SHALLOW_TRENCH");

        var window = new ProcessManagementWindow { DataContext = vm, Height = WindowHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        using var bitmap = window.CaptureRenderedFrame();
        window.Close();
        Dispatcher.UIThread.RunJobs();

        bitmap.ShouldNotBeNull("render miss for the process editor");
        var directory = ScreenshotArtifacts.ResolvePrMediaDirectory("waveguide-companion-layers");
        Directory.CreateDirectory(directory);
        ScreenshotArtifacts.SavePng(bitmap!, Path.Combine(directory, "04-process-editor-companion-layers.png"))
            .Length.ShouldBeGreaterThan(0);
    }
}
