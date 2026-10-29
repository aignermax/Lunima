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
/// MainWindow with all sixteen connections selected, before and after one click — and a tangled
/// variant that is re-laid as a bundle. PNGs land in
/// <c>docs/pr-media/length-matching/</c> with <c>CAP_UPDATE_PR_MEDIA=1</c>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class LengthEqualizationScreenshotTests
{
    private static readonly (double X, double Y, double W, double H) Region = (100, 250, 1800, 2000);
    private static readonly (double X, double Y, double W, double H) BundleRegion = (100, 250, 1800, 2700);

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

    /// <summary>
    /// The same fan-in after half of its routes were stretched into long meanders: equalizing
    /// re-lays the whole bundle as nested arches at a common length below the tangled maximum.
    /// </summary>
    [AvaloniaFact]
    public async Task CaptureTangledBundleRelaid()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("length-matching");
        Directory.CreateDirectory(dir);
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

        var vm = ShowcaseCircuit.CreateStagedViewModel();
        vm.Canvas.CommandManager = vm.CommandManager;
        var connections = await FiberArrayExampleBuilder.BuildAsync(vm);
        Tangle(vm, connections.Where((_, i) => i % 2 == 0).Select(c => c.Connection).ToList());
        double tangledLongest = connections.Max(c => c.Connection.PathLengthMicrometers);
        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            foreach (var connection in connections)
                vm.Canvas.Selection.SelectedConnections.Add(connection);
            ShowcaseCircuit.SetView(window, vm, BundleRegion);
            ShowcaseCapture.PumpRenderLoop();
            ShowcaseCapture.CaptureWindow(window, Path.Combine(dir, "length-matching-4-bundle-tangled.png"));

            await vm.BottomPanel.LengthEqualization.EqualizeCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            var panel = vm.BottomPanel.LengthEqualization;
            panel.HasSuccessStatus.ShouldBeTrue(panel.StatusMessage);
            connections.Max(c => c.Connection.PathLengthMicrometers).ShouldBeLessThan(tangledLongest);
            ShowcaseCapture.PumpRenderLoop();
            ShowcaseCapture.CaptureWindow(window, Path.Combine(dir, "length-matching-5-bundle-relaid.png"));

            // One route selected: its padlock and the glow on every route locked to the same length.
            vm.Canvas.Selection.SelectedConnections.Clear();
            foreach (var connection in connections)
                connection.IsSelected = false;
            connections[5].IsSelected = true;
            vm.CanvasInteraction.SelectedWaveguideConnection = connections[5];
            Dispatcher.UIThread.RunJobs();
            ShowcaseCircuit.SetView(window, vm, BundleRegion);
            ShowcaseCapture.PumpRenderLoop();
            ShowcaseCapture.CaptureWindow(window, Path.Combine(dir, "length-matching-6-lock-group.png"));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }
    }

    private static void Tangle(CAP.Avalonia.ViewModels.MainViewModel vm, IReadOnlyList<CAP_Core.Components.Connections.WaveguideConnection> routes)
    {
        const double ExtraMicrometers = 900;
        var plan = new CAP_Core.Routing.LengthEqualization.LengthEqualizer(vm.Canvas.Router).Prepare(routes,
            vm.Canvas.ConnectionManager.Connections.ToList(), vm.Canvas.Components.Select(c => c.Component).ToList(),
            routes.Max(c => c.PathLengthMicrometers) + ExtraMicrometers, 10, 0.1);
        foreach (var outcome in CAP_Core.Routing.LengthEqualization.LengthEqualizer.Solve(plan).Outcomes.Where(o => o.IsSuccess))
        {
            outcome.Connection.ReplaceRoutedPath(outcome.Path!);
            outcome.Connection.IsRouteFrozen = true;
        }
    }
}
