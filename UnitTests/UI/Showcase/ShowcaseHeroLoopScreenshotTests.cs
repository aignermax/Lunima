using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels.Workspaces;
using Shouldly;
using UnitTests.UI.Flows;
using Xunit;

namespace UnitTests.UI.Showcase;

/// <summary>
/// Feature showcase: the README hero loop (<c>hero-loop.gif</c>). Stages the hero chip LIVE
/// in the real MainWindow (workspace bar visible): every photonic component placed one by
/// one, the production router wiring connection after connection (diagonal pathfinding
/// on), the DC pads and golden metal traces, the two hand-styled bends — then the switch
/// to the Optics workspace and Run lighting up the real CW power-flow overlay.
/// Opt-in via <c>UI_SHOT_DIR</c>; the GIF lands in <c>UI_SHOT_DIR/v0.17/</c>.
/// </summary>
[Trait("Category", "Showcase")]
[Collection("LocalizationSingleton")]
public class ShowcaseHeroLoopScreenshotTests
{
    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;
    private const int EmptyHoldMs = 400;
    private const int PlaceMs = 80;
    private const int RouteMs = 130;
    private const int RoutedHoldMs = 500;
    private const int WorkspaceSwitchMs = 450;
    private const int PayoffHoldMs = 1700;

    [AvaloniaFact]
    public async Task CaptureHeroLoop()
    {
        if (!ShowcaseCapture.Enabled) return;
        await ShowcaseCapture.WithEnglishUiAsync(async () =>
        {
            var vm = ShowcaseCircuit.CreateStagedViewModel();
            var window = ShowcaseCircuit.BootMainWindow(vm, WindowWidth, WindowHeight);
            UiInput.Descendants<Border>(window)
                .First(b => b.Name == "RightPanelBorder").IsVisible = false;
            vm.WorkspaceSwitch.Current = Workspace.Layout;
            Dispatcher.UIThread.RunJobs();
            ShowcaseCircuit.FitView(window, vm);

            var gif = new ShowcaseGifRecorder("hero-loop", window,
                ShowcaseFraming.WindowDownToCanvasBottom(window));
            gif.Capture(EmptyHoldMs);
            await ShowcaseCircuit.BuildChipAsync(vm,
                afterStep: () => { gif.Capture(RouteMs); return Task.CompletedTask; },
                afterPlace: () => { gif.Capture(PlaceMs); return Task.CompletedTask; });
            gif.Hold(RoutedHoldMs);

            vm.WorkspaceSwitch.Current = Workspace.Optics;
            Dispatcher.UIThread.RunJobs();
            gif.Capture(WorkspaceSwitchMs);
            await vm.RunWorkspaceCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            vm.Canvas.ShowPowerFlow.ShouldBeTrue("Run in Optics must switch the power-flow overlay on");
            gif.Capture(PayoffHoldMs);

            await gif.EncodeAsync();
            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }
}
