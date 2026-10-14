using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.UI.Showcase;

/// <summary>
/// Feature showcase: <c>live-simulation.gif</c>. The hero Mach-Zehnder chip in the Optics
/// workspace with the CW simulation switched on: the combiner→photodetector wire is
/// missing, connecting it lights the detector arm up on its own (the live re-simulation),
/// then sweeping the phase shifter's slider once around 0–360° swings the light between
/// the detector and the output coupler — every frame is a real, settled live re-run.
/// Opt-in via <c>UI_SHOT_DIR</c>; the GIF lands in <c>UI_SHOT_DIR/v0.17/</c>.
/// </summary>
[Trait("Category", "Showcase")]
[Collection("LocalizationSingleton")]
public class ShowcaseLiveSimulationGifTests
{
    private const int WindowWidth = 1600;
    private const int WindowHeight = 640;
    private const int MissingWireHoldMs = 900;
    private const int RoutedMs = 250;
    private const int LitHoldMs = 900;
    private const int SelectedMs = 500;
    private const int SweepStepMs = 200;
    private const int SweepEndHoldMs = 900;
    private const double SweepStepDegrees = 30;
    private const double FullTurnDegrees = 360;

    [AvaloniaFact]
    public async Task CaptureLiveSimulation()
    {
        if (!ShowcaseCapture.Enabled) return;
        await ShowcaseCapture.WithEnglishUiAsync(async () =>
        {
            var vm = ShowcaseCircuit.CreateStagedViewModel();
            await ShowcaseCircuit.BuildChipAsync(vm);
            var detector = ComponentByTemplate(vm, "Photodetector");
            var shifter = ComponentByTemplate(vm, "Phase Shifter");
            var combiner = ComponentByTemplate(vm, "2x2 MMI Coupler");
            vm.CommandManager.ExecuteCommand(new DeleteConnectionCommand(vm.Canvas, WireInto(vm, detector, "in")));

            var window = ShowcaseCircuit.BootMainWindow(vm, WindowWidth, WindowHeight);
            vm.WorkspaceSwitch.Current = Workspace.Optics;
            Dispatcher.UIThread.RunJobs();
            ShowcaseCircuit.FitView(window, vm);
            await ShowcaseCircuit.WaitForRoutingIdleAsync(vm.Canvas);
            await vm.RunWorkspaceCommand.ExecuteAsync(null);
            vm.Canvas.ShowPowerFlow.ShouldBeTrue("Run in Optics switches the simulation on");
            await SettleAsync(vm);

            var gif = new ShowcaseGifRecorder("live-simulation", window, CanvasAndProperties(window, vm));
            gif.Capture(MissingWireHoldMs);

            var added = await vm.Canvas.ConnectPinsAsync(
                Pin(combiner.Component, "out1"), Pin(detector.Component, "in"));
            added.ShouldNotBeNull("the combiner→detector wire must route");
            gif.Capture(RoutedMs);
            await SettleAsync(vm);
            (vm.Canvas.PowerFlowVisualizer.GetFlowForConnection(added!.Connection.Id)?.AveragePower ?? 0)
                .ShouldBeGreaterThan(0, "the live re-run lights up the new wire");
            gif.Capture(LitHoldMs);

            vm.CanvasInteraction.SelectedComponent = shifter;
            Dispatcher.UIThread.RunJobs();
            gif.Capture(SelectedMs);
            var slider = window.GetVisualDescendants().OfType<Border>()
                .First(b => b.Name == "RightPanelBorder")
                .GetVisualDescendants().OfType<global::Avalonia.Controls.Slider>().First(s => s.IsEffectivelyVisible);
            double start = slider.Value;
            for (double turn = SweepStepDegrees; turn <= FullTurnDegrees; turn += SweepStepDegrees)
            {
                // Through the properties panel's slider control, as a user dragging it.
                slider.Value = (start + turn) % FullTurnDegrees;
                await SettleAsync(vm);
                gif.Capture(SweepStepMs);
            }
            gif.Hold(SweepEndHoldMs - SweepStepMs);

            await gif.EncodeAsync();
            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    /// <summary>From the canvas' left edge to the window's right edge (properties column
    /// with the slider included), toolbar down to just below the chip.</summary>
    private static PixelRect CanvasAndProperties(Window window, MainViewModel vm)
    {
        var canvas = ShowcaseCapture.BoundsIn(window, ShowcaseFraming.Canvas(window));
        int bottom = ShowcaseFraming.CanvasWorld(window, vm, ChipWithMargin).Bottom;
        return new PixelRect(canvas.X, 0, (int)window.Bounds.Width - canvas.X, bottom);
    }

    /// <summary>World-space (µm) extent of the staged chip plus a little floor below it.</summary>
    private static readonly (double X, double Y, double W, double H) ChipWithMargin = (0, 0, 1530, 640);

    /// <summary>Waits until the debounced live re-simulation has run and the overlay repainted.</summary>
    private static async Task SettleAsync(MainViewModel vm)
    {
        for (int round = 0; round < SettleRounds; round++)
        {
            await Task.Delay(SettlePollMs);
            Dispatcher.UIThread.RunJobs();
            await vm.LiveSimulation.WhenSettledAsync();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Polls span the scheduler's 150 ms debounce plus a routing pass that may end
    /// in between and queue one more run.</summary>
    private const int SettleRounds = 3;
    private const int SettlePollMs = 200;

    private static ComponentViewModel ComponentByTemplate(MainViewModel vm, string templateName) =>
        vm.Canvas.Components.First(c => c.TemplateName == templateName);

    private static WaveguideConnectionViewModel WireInto(MainViewModel vm, ComponentViewModel component, string pin) =>
        vm.Canvas.Connections.First(c =>
            c.Connection.EndPin == Pin(component.Component, pin) || c.Connection.StartPin == Pin(component.Component, pin));

    private static PhysicalPin Pin(Component component, string name) =>
        component.PhysicalPins.First(p => p.Name == name);
}
