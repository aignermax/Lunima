using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Analysis.LogicAnalysis.GateBuilding;
using CAP_Core.Components.Process;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.UI.Showcase;

/// <summary>
/// Feature showcase: <c>make-gate.gif</c> — the shipped "Build your first gate" kit in the
/// Logic workspace: the parts are box-selected, <c>Make gate</c> groups them and opens the
/// gate steps, the real optical simulation fills the truth table and the automatic
/// threshold confirms a NAND. Self-contained so it can be re-rendered alone:
/// <c>--filter FullyQualifiedName~ShowcaseMakeGateGif</c>. Opt-in via <c>UI_SHOT_DIR</c>.
/// </summary>
[Trait("Category", "Showcase")]
[Collection("LocalizationSingleton")]
public class ShowcaseMakeGateGifTests
{
    private const string GateKitExample = "Logic Gate - Build your first gate.lun";
    private const int WindowWidth = 1440;
    private const int WindowHeight = 860;
    private const double RightPanelWidth = 400;
    private const double SelectionMarginMicrometers = 30;
    private const double FitMarginPx = 50;
    private const int KitHoldMs = 700;
    private const int BoxGrowMs = 160;
    private const int SelectedHoldMs = 500;
    private const int StepsHoldMs = 700;
    private const int ResultHoldMs = 2000;
    private static readonly double[] BoxGrowth = { 0.35, 0.7, 1.0 };

    [AvaloniaFact]
    public async Task CaptureMakeGate()
    {
        if (!ShowcaseCapture.Enabled) return;
        await ShowcaseCapture.WithEnglishUiAsync(async () =>
        {
            var vm = ShowcaseCircuit.CreateStagedViewModel();
            var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), GateKitExample);
            (await vm.FileOperations.OpenDesignAsCopyAsync(path)).ShouldBeTrue($"'{GateKitExample}' must load");
            await vm.FileOperations.PostLoadRouting;
            vm.FileOperations.SetActiveProcess(ActiveProcessSelection.Playground(), markDirty: false);
            vm.WorkspaceSwitch.Current = Workspace.Logic;

            var window = ShowcaseCircuit.BootMainWindow(vm, WindowWidth, WindowHeight);
            ShowcaseFraming.SetRightPanelWidth(window, RightPanelWidth);
            await ShowcaseCircuit.WaitForRoutingIdleAsync(vm.Canvas);
            ShowcaseFraming.FitComponents(window, vm, FitMarginPx);

            var gif = new ShowcaseGifRecorder("make-gate", window,
                ShowcaseFraming.WindowWithoutStatusBar(window));
            gif.Capture(KitHoldMs);
            BoxSelectEverything(window, vm, gif);
            gif.Capture(SelectedHoldMs);

            vm.MakeGateCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var makeGate = vm.RightPanel.MakeGate;
            makeGate.IsActive.ShouldBeTrue(vm.StatusText);
            ShowcaseFraming.ScrollRightPanelTo(window, ShowcaseFraming.Named<Avalonia.Controls.Control>(window, "MakeGateSimulateButton"));
            gif.Capture(StepsHoldMs);

            await makeGate.SimulateCommand.ExecuteAsync(null);
            makeGate.HasResult.ShouldBeTrue(makeGate.StatusText);
            makeGate.SelectedFunction = makeGate.Functions.Single(f => f.Function == GateFunction.Nand);
            makeGate.MatchesFunction.ShouldBeTrue("the kit is a NAND");
            Dispatcher.UIThread.RunJobs();
            ShowcaseFraming.ScrollRightPanelTo(window, ShowcaseFraming.Named<Avalonia.Controls.Control>(window, "MakeGateSaveButton"));
            gif.Capture(ResultHoldMs);

            await gif.EncodeAsync();
            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    /// <summary>Draws the rubber-band box growing over the kit (the canvas renders the
    /// <see cref="CAP.Avalonia.Selection.SelectionManager"/> box state), then applies the
    /// same rectangle selection a mouse release does.</summary>
    private static void BoxSelectEverything(Avalonia.Controls.Window window, MainViewModel vm, ShowcaseGifRecorder gif)
    {
        var bounds = BoundingBoxCalculator.Calculate(vm.Canvas.Components);
        bounds.ShouldNotBeNull("the kit has parts");
        var box = bounds!.Value;
        double minX = box.MinX - SelectionMarginMicrometers, minY = box.MinY - SelectionMarginMicrometers;
        double maxX = box.MaxX + SelectionMarginMicrometers, maxY = box.MaxY + SelectionMarginMicrometers;

        var selection = vm.Canvas.Selection;
        selection.IsBoxSelecting = true;
        selection.BoxStartX = minX;
        selection.BoxStartY = minY;
        foreach (double t in BoxGrowth)
        {
            selection.BoxEndX = minX + (maxX - minX) * t;
            selection.BoxEndY = minY + (maxY - minY) * t;
            ShowcaseFraming.Canvas(window).InvalidateVisual();
            gif.Capture(BoxGrowMs);
        }
        selection.IsBoxSelecting = false;
        selection.SelectInRectangle(vm.Canvas.Components, minX, minY, maxX, maxY);
        selection.SelectedComponents.Count.ShouldBeGreaterThan(1);
        Dispatcher.UIThread.RunJobs();
    }
}
