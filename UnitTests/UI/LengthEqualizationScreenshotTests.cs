using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services.Localization;
using Shouldly;
using UnitTests.Routing.LengthEqualization;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for "Equalize lengths": the shipped fibre-array example in the real
/// MainWindow with all sixteen connections selected, before and after one click. PNGs land in
/// <c>docs/pr-media/length-matching/</c> with <c>CAP_UPDATE_PR_MEDIA=1</c>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class LengthEqualizationScreenshotTests
{
    private static readonly (double X, double Y, double W, double H) Region = (100, 250, 1800, 2000);

    [AvaloniaFact]
    public async Task CaptureBeforeAndAfterEqualizeLengths()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("length-matching");
        Directory.CreateDirectory(dir);
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

        var vm = ShowcaseCircuit.CreateStagedViewModel();
        vm.Canvas.CommandManager = vm.CommandManager;
        var connections = await FiberArrayExampleBuilder.BuildAsync(vm);
        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            foreach (var connection in connections)
                vm.Canvas.Selection.SelectedConnections.Add(connection);
            ShowcaseCircuit.SetView(window, vm, Region);
            ShowcaseCapture.PumpRenderLoop();
            ShowcaseCapture.CaptureWindow(window, Path.Combine(dir, "length-matching-2-equalize-before.png"));

            await vm.BottomPanel.LengthEqualization.EqualizeCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            vm.BottomPanel.LengthEqualization.HasSuccessStatus.ShouldBeTrue(vm.BottomPanel.LengthEqualization.StatusMessage);
            ShowcaseCapture.PumpRenderLoop();
            ShowcaseCapture.CaptureWindow(window, Path.Combine(dir, "length-matching-3-equalize-after.png"));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }
    }
}
