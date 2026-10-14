using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.UI.Showcase;

/// <summary>
/// Feature showcase <c>chiplets.gif</c>: the shipped two-chiplet example in the System
/// workspace with the simulation on — light crosses the butt-coupled edge-coupler link
/// from the transmitter die to the receiver die. The receiver die is dragged off its
/// partner in a few steps (the live simulation re-runs after each and the link loses
/// light), Design Checks flag the misaligned link, and the one-click "Align chiplet" fix
/// snaps the die back into butt-coupling — the light returns. Everything runs through the
/// production commands (undoable group move, <c>RunDesignChecks</c>, <c>AlignChiplet</c>).
/// Opt-in via <c>UI_SHOT_DIR</c>.
/// </summary>
[Trait("Category", "Showcase")]
[Collection("LocalizationSingleton")]
public class ShowcaseChipletsGifTests
{
    private const string ExampleFile = "Two Chiplets - Edge-Coupler Link.lun";
    private const string ReceiverChiplet = "Receiver Chiplet";
    private const int WindowWidth = 1280;
    private const int WindowHeight = 700;
    private const double DockHeight = 230;
    private const double ViewPaddingUm = 40;
    private const int DragSteps = 4;
    private const double DragStepXUm = 6;
    private const double DragStepYUm = 0.5;
    private const int LiveMs = 1300;
    private const int DragStepMs = 260;
    private const int WarningMs = 1700;
    private const int AlignedHoldMs = 2200;
    private static readonly TimeSpan SimulationSettle = TimeSpan.FromSeconds(20);

    [AvaloniaFact]
    public async Task CaptureChipletAlignment()
    {
        if (!ShowcaseCapture.Enabled) return;
        await ShowcaseCapture.WithEnglishUiAsync(async () =>
        {
            var vm = ShowcaseCircuit.CreateStagedViewModel();
            var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFile);
            (await vm.FileOperations.OpenDesignAsCopyAsync(path)).ShouldBeTrue($"'{ExampleFile}' must load");
            await vm.FileOperations.PostLoadRouting;
            var window = ShowcaseCircuit.BootMainWindow(vm, WindowWidth, WindowHeight);
            ShowcaseAgentStage.HideRightPanel(window);
            vm.WorkspaceSwitch.Current.ShouldBe(Workspace.System, "the example opens in System");
            vm.BottomPanel.Analysis.SetDockHeight(DockHeight);
            Dispatcher.UIThread.RunJobs();
            var view = DesignRegion(vm);

            await vm.RunWorkspaceCommand.ExecuteAsync(null);
            vm.Canvas.ShowPowerFlow.ShouldBeTrue("Run in System switches the simulation on");
            var gif = new ShowcaseGifRecorder("chiplets", window, ShowcaseFraming.WindowWithoutStatusBar(window));
            void Capture(int ms)
            {
                vm.RefreshSystemOverview();
                ShowcaseCapture.PumpRenderLoop();
                ShowcaseCircuit.SetView(window, vm, view);
                gif.Capture(ms);
            }
            Capture(LiveMs);

            var receiver = vm.Canvas.Components.Single(c => c.Component is ComponentGroup g && g.GroupName == ReceiverChiplet);
            for (int step = 0; step < DragSteps; step++)
            {
                DragChiplet(vm, receiver, DragStepXUm, DragStepYUm);
                await WaitForLiveSimulationAsync(vm);
                Capture(DragStepMs);
            }

            await vm.RunDesignChecksCommand.ExecuteAsync(null);
            vm.BottomPanel.Analysis.OpenChecks();
            var validation = vm.RightPanel.DesignValidation;
            NavigateToChipletIssue(validation);
            validation.IsCurrentIssueAlignable.ShouldBeTrue("the misaligned link offers Align chiplet");
            Capture(WarningMs);

            await validation.AlignChipletCommand.ExecuteAsync(null);
            await vm.Canvas.RecalculateRoutesAsync();
            await WaitForLiveSimulationAsync(vm);
            await vm.RunDesignChecksCommand.ExecuteAsync(null);
            validation.Issues.ShouldNotContain(i => i.Type.ToString().StartsWith("ChipletInterface"),
                "Align chiplet must clear the link finding");
            Capture(AlignedHoldMs);

            await gif.EncodeAsync();
            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    /// <summary>World region (µm) around both dies, widened by the drag so the receiver
    /// stays in frame while it is pulled away.</summary>
    private static (double X, double Y, double W, double H) DesignRegion(MainViewModel vm)
    {
        var box = CAP.Avalonia.ViewModels.Canvas.BoundingBoxCalculator.Calculate(vm.Canvas.Components)!.Value;
        double dragX = DragSteps * DragStepXUm, dragY = DragSteps * DragStepYUm;
        return (box.MinX - ViewPaddingUm, box.MinY - ViewPaddingUm,
            box.Width + dragX + 2 * ViewPaddingUm, box.Height + dragY + 2 * ViewPaddingUm);
    }

    /// <summary>One step of a user drag of the die: the canvas drag choreography plus the
    /// undoable group move a drag-drop records.</summary>
    private static void DragChiplet(MainViewModel vm,
        CAP.Avalonia.ViewModels.Canvas.ComponentViewModel chiplet, double dx, double dy)
    {
        vm.Canvas.BeginDragComponent(chiplet);
        vm.Canvas.MoveComponent(chiplet, dx, dy);
        vm.CommandManager.ExecuteCommand(new GroupMoveCommand(vm.Canvas, new[] { chiplet }, dx, dy));
        vm.Canvas.EndDragComponent(chiplet);
    }

    /// <summary>Lets the debounced live re-run (triggered by the command) finish.</summary>
    private static async Task WaitForLiveSimulationAsync(MainViewModel vm)
    {
        await vm.Canvas.RecalculateRoutesAsync();
        var deadline = DateTime.UtcNow + SimulationSettle;
        do
        {
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
        }
        while ((vm.IsSimulating || vm.Canvas.IsRouting) && DateTime.UtcNow < deadline);
        await Task.Delay(400);
        Dispatcher.UIThread.RunJobs();
    }

    private static void NavigateToChipletIssue(CAP.Avalonia.ViewModels.Diagnostics.DesignValidationViewModel validation)
    {
        for (int i = 0; i < validation.Issues.Count; i++)
        {
            if (validation.Issues[validation.CurrentIndex].Type.ToString().StartsWith("ChipletInterface"))
                return;
            validation.NextIssueCommand.Execute(null);
        }
        throw new ShouldAssertException("no chiplet-interface issue to navigate to");
    }
}
