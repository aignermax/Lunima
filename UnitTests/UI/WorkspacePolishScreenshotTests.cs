using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP.Avalonia.Views;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using Shouldly;
using UnitTests.Integration;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation of the workspace identity: the full window in each of Layout /
/// Optics / Logic / System, showing the workspace hue (switch segment, accent line, canvas
/// tint, status chip, Run) and the contextual left rail (library, sources quick section,
/// Gates, Chiplets). Logic selects its gate so the Truth Table section is visible.
/// Opt-in (UI_SHOT_DIR); PNGs land in <c>docs/pr-media/workspace-polish/</c> with
/// <c>CAP_UPDATE_PR_MEDIA=1</c>, otherwise in a temp directory.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class WorkspacePolishScreenshotTests
{
    private const int CaptureAttempts = 3;
    private const double DockHeight = 200;

    /// <summary>One full-window capture per workspace on a fitting shipped example.</summary>
    [AvaloniaFact]
    public async Task CaptureWorkspaceIdentity()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UI_SHOT_DIR")))
            return;
        var outputDir = ScreenshotArtifacts.ResolvePrMediaDirectory("workspace-polish");
        Directory.CreateDirectory(outputDir);
        foreach (var stale in Directory.GetFiles(outputDir, "*.png"))
            File.Delete(stale);

        await Capture(outputDir, "EBeam Mach-Zehnder Interferometer.lun", Workspace.Layout, "01-layout.png");
        await Capture(outputDir, "EBeam Mach-Zehnder Interferometer.lun", Workspace.Optics, "02-optics.png");
        await Capture(outputDir, "Logic Gate NOT-NAND.lun", Workspace.Logic, "03-logic.png", selectGate: true);
        await Capture(outputDir, "Two Chiplets - Edge-Coupler Link.lun", Workspace.System, "04-system.png");

        Directory.GetFiles(outputDir, "*.png").Length.ShouldBe(4);
    }

    private static async Task Capture(string outputDir, string fileName, Workspace workspace, string pngName,
        bool selectGate = false)
    {
        var vm = ShowcaseCircuit.CreateStagedViewModel();
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), fileName);
        (await vm.FileOperations.OpenDesignAsCopyAsync(path)).ShouldBeTrue($"'{fileName}' must load");
        await vm.FileOperations.PostLoadRouting;
        vm.FileOperations.SetActiveProcess(ActiveProcessSelection.Playground(), markDirty: false);
        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            vm.Home.IsHomeVisible = false;
            vm.WorkspaceSwitch.Current = workspace;
            vm.BottomPanel.Analysis.SetDockHeight(DockHeight);
            Dispatcher.UIThread.RunJobs();
            await vm.RunWorkspaceCommand.ExecuteAsync(null);
            await ShowcaseCircuit.WaitForRoutingIdleAsync(vm.Canvas);
            if (selectGate)
                SelectFirstGate(vm);
            Dispatcher.UIThread.RunJobs();
            ShowcaseCircuit.FitView(window, vm);
            Save(window, Path.Combine(outputDir, pngName));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void SelectFirstGate(MainViewModel vm)
    {
        var gate = vm.Canvas.Components.First(c => c.Component is ComponentGroup { TruthTablePinAssignment: not null });
        vm.Canvas.Selection.SelectSingle(gate);
        vm.Canvas.SelectedComponent = gate;
        vm.RightPanel.TruthTable.ConfigureForSelection(gate, vm.Canvas);
        vm.IsGateSectionVisible.ShouldBeTrue();
    }

    /// <summary>Pumps layout + render, then captures the window, keeping the last good frame.</summary>
    private static void Save(MainWindow window, string path)
    {
        ScreenshotArtifacts.SettleAnimations();
        WriteableBitmap? bitmap = null;
        for (int attempt = 0; attempt < CaptureAttempts; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var frame = window.CaptureRenderedFrame();
            if (frame == null)
                continue;
            bitmap?.Dispose();
            bitmap = frame;
        }

        bitmap.ShouldNotBeNull($"no frame captured for {path}");
        using (bitmap)
        {
            ScreenshotArtifacts.SavePng(bitmap, path);
        }
    }
}
