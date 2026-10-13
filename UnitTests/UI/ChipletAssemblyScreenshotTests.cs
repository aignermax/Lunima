using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP.Avalonia.Views;
using CAP_Core.Components.Dies;
using Shouldly;
using UnitTests.Integration;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation of dies on an assembly: the shipped SOI + SiN example in the System
/// workspace (per-die frames in process colours, Chiplets panel with link loss), a selected
/// die with its properties, a selected link, and the inside of a die (frame = die outline,
/// library scoped to the die's process). Opt-in (UI_SHOT_DIR); PNGs land in
/// <c>docs/pr-media/chiplet-assembly/</c> with <c>CAP_UPDATE_PR_MEDIA=1</c>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class ChipletAssemblyScreenshotTests
{
    private const int CaptureAttempts = 3;
    private const double DockHeight = 200;
    private const string Example = "Two Dies - SOI and SiN Edge-Coupler Link.lun";

    [AvaloniaFact]
    public async Task CaptureAssembly()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UI_SHOT_DIR")))
            return;
        var outputDir = ScreenshotArtifacts.ResolvePrMediaDirectory("chiplet-assembly");
        Directory.CreateDirectory(outputDir);

        var vm = ShowcaseCircuit.CreateStagedViewModel();
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), Example);
        (await vm.FileOperations.OpenDesignAsCopyAsync(path)).ShouldBeTrue();
        await vm.FileOperations.PostLoadRouting;
        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            vm.Home.IsHomeVisible = false;
            vm.WorkspaceSwitch.Current = Workspace.System;
            vm.BottomPanel.Analysis.SetDockHeight(DockHeight);
            await vm.RunWorkspaceCommand.ExecuteAsync(null);
            FitAssembly(window, vm);
            Capture(window, Path.Combine(outputDir, "10-example-soi-sin-system.png"));

            var sin = vm.Canvas.Dies.RootDies.Single(d => d.GroupName == "SiN die");
            var sinVm = vm.Canvas.Components.Single(c => c.Component == sin);
            vm.Canvas.Selection.SelectSingle(sinVm);
            vm.Canvas.SelectedComponent = sinVm;
            Dispatcher.UIThread.RunJobs();
            vm.DieProperties.IsVisible.ShouldBeTrue();
            Capture(window, Path.Combine(outputDir, "11-sin-die-properties.png"));

            var link = vm.Canvas.Connections.Single(c => c.Connection.IsCrossChipletFacetLink);
            vm.Canvas.SelectedComponent = null;
            vm.CanvasInteraction.SelectedWaveguideConnection = link;
            Dispatcher.UIThread.RunJobs();
            vm.DieLinkProperties.IsVisible.ShouldBeTrue();
            var linkRegion = DieAssembly.OutlineOf(sin);
            ShowcaseCircuit.SetView(window, vm, (linkRegion.X - 450, linkRegion.Y + 150, 900, 400));
            Capture(window, Path.Combine(outputDir, "12-link-properties.png"));

            vm.CanvasInteraction.SelectedWaveguideConnection = null;
            vm.OpenDie(sin);
            Dispatcher.UIThread.RunJobs();
            vm.Canvas.Dies.ActiveDie.ShouldBe(sin);
            ShowcaseCircuit.SetView(window, vm, (linkRegion.X - 100, linkRegion.Y - 100, linkRegion.Width + 200, linkRegion.Height + 200));
            vm.LeftRail.ShowLibraryCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Capture(window, Path.Combine(outputDir, "13-inside-sin-die.png"));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void FitAssembly(MainWindow window, MainViewModel vm)
    {
        var all = DieAssembly.CarrierOutline(vm.Canvas.Dies.RootDies)!.Value;
        ShowcaseCircuit.SetView(window, vm, (all.X - 150, all.Y - 150, all.Width + 300, all.Height + 300));
    }

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
            ScreenshotArtifacts.SavePng(bitmap, path);
    }
}
