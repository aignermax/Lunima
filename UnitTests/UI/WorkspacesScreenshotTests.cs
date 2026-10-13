using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP.Avalonia.Views;
using CAP_Core.Components.Process;
using Shouldly;
using UnitTests.Integration;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation of the workspace switch: the toolbar (switch, Run label, overlay
/// eye) and the analysis dock in each of Layout / Optics / Logic / System, the System
/// chiplet strip, the self-explaining empty tabs and the "show all tabs" override.
/// Opt-in (UI_SHOT_DIR); PNGs land in <c>docs/pr-media/workspaces/</c> with
/// <c>CAP_UPDATE_PR_MEDIA=1</c>, otherwise in a temp directory.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class WorkspacesScreenshotTests
{
    private const int CaptureAttempts = 3;
    private const double DockHeight = 230;

    /// <summary>One capture per workspace on a fitting shipped example, plus the empty states.</summary>
    [AvaloniaFact]
    public async Task CaptureWorkspaces()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UI_SHOT_DIR")))
            return;
        var outputDir = ScreenshotArtifacts.ResolvePrMediaDirectory("workspaces");
        Directory.CreateDirectory(outputDir);
        foreach (var stale in Directory.GetFiles(outputDir, "*.png"))
            File.Delete(stale);

        await CaptureExample(outputDir, "EBeam Mach-Zehnder Interferometer.lun", Workspace.Layout, "01-layout-run-checks.png");
        await CaptureExample(outputDir, "EBeam Mach-Zehnder Interferometer.lun", Workspace.Optics, "02-optics-simulate.png");
        await CaptureExample(outputDir, "Logic Gate NOT-NAND.lun", Workspace.Logic, "03-logic-run.png");
        await CaptureExample(outputDir, "Two Chiplets - Edge-Coupler Link.lun", Workspace.System, "04-system-chiplets.png");
        CaptureEmptyStates(outputDir);

        Directory.GetFiles(outputDir, "*.png").Length.ShouldBeGreaterThanOrEqualTo(7);
    }

    private static async Task CaptureExample(string outputDir, string fileName, Workspace workspace, string pngName)
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
            vm.BottomPanel.Analysis.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            ShowcaseCircuit.FitView(window, vm);
            Capture(window, Path.Combine(outputDir, pngName));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Empty design: the optical tabs ask for a light source; with one, Transient asks for an output.</summary>
    private static void CaptureEmptyStates(string outputDir)
    {
        var vm = ShowcaseCircuit.CreateStagedViewModel();
        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            vm.Home.IsHomeVisible = false;
            vm.WorkspaceSwitch.Current = Workspace.Optics;
            vm.BottomPanel.Analysis.SetDockHeight(DockHeight);
            vm.BottomPanel.Analysis.Open(AnalysisDockTab.Spectrum);
            Capture(window, Path.Combine(outputDir, "05-empty-no-light-source.png"));

            var source = TestComponentFactory.CreateBasicComponent();
            source.IsUserMarkedLightSource = true;
            source.PhysicalX = 200;
            source.PhysicalY = 200;
            vm.Canvas.AddComponent(source);
            vm.BottomPanel.Analysis.Open(AnalysisDockTab.Transient);
            Capture(window, Path.Combine(outputDir, "06-empty-pick-output.png"));

            vm.BottomPanel.Analysis.ShowAllTabs = true;
            Capture(window, Path.Combine(outputDir, "07-show-all-tabs.png"));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Pumps layout + render, then captures the window, keeping the last good frame.</summary>
    private static void Capture(MainWindow window, string path)
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
