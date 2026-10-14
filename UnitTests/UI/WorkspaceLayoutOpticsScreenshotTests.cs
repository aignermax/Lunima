using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP.Avalonia.Views;
using CAP_Core.Components.Process;
using Shouldly;
using UnitTests.Integration;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation of the Layout / Optics split on the same design (the shipped MZI):
/// Optics after Run (simulation on, light emphasised, optical dock tab), then Layout after
/// Run (design checks, geometry emphasised, power overlay hidden). Opt-in (UI_SHOT_DIR);
/// PNGs land in <c>docs/pr-media/workspace-layout-optics/</c> with <c>CAP_UPDATE_PR_MEDIA=1</c>,
/// otherwise in a temp directory. <c>WS_SHOT_PREFIX</c> prefixes the file names (before/after).
/// </summary>
[Trait("Category", "UiScreenshots")]
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class WorkspaceLayoutOpticsScreenshotTests
{
    private const int CaptureAttempts = 3;
    private const double DockHeight = 230;
    private const string DefaultExampleFile = "Mach-Zehnder Interferometer.lun";

    /// <summary>Optics, then Layout, on the MZI example — the same window, switched once.</summary>
    [AvaloniaFact]
    public async Task CaptureLayoutVersusOptics()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UI_SHOT_DIR")))
            return;
        var outputDir = ScreenshotArtifacts.ResolvePrMediaDirectory("workspace-layout-optics");
        Directory.CreateDirectory(outputDir);
        var prefix = Environment.GetEnvironmentVariable("WS_SHOT_PREFIX") ?? "after";
        var exampleFile = Environment.GetEnvironmentVariable("WS_SHOT_EXAMPLE") ?? DefaultExampleFile;

        var vm = ShowcaseCircuit.CreateStagedViewModel();
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), exampleFile);
        (await vm.FileOperations.OpenDesignAsCopyAsync(path)).ShouldBeTrue();
        await vm.FileOperations.PostLoadRouting;
        vm.FileOperations.SetActiveProcess(ActiveProcessSelection.Playground(), markDirty: false);
        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            vm.Home.IsHomeVisible = false;
            vm.BottomPanel.Analysis.SetDockHeight(DockHeight);
            vm.BottomPanel.Analysis.IsVisible = true;

            await ShowWorkspace(window, vm, Workspace.Optics);
            Capture(window, Path.Combine(outputDir, $"{prefix}-optics.png"));

            await ShowWorkspace(window, vm, Workspace.Layout);
            vm.Canvas.ViewSettings.HasDesignCheckFindings.ShouldBeTrue("Layout marks the check findings");
            Capture(window, Path.Combine(outputDir, $"{prefix}-layout.png"));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task ShowWorkspace(MainWindow window, CAP.Avalonia.ViewModels.MainViewModel vm, Workspace workspace)
    {
        vm.WorkspaceSwitch.Current = workspace;
        Dispatcher.UIThread.RunJobs();
        if (!vm.WorkspaceSwitch.IsRunActive)
            await vm.RunWorkspaceCommand.ExecuteAsync(null);
        await ShowcaseCircuit.WaitForRoutingIdleAsync(vm.Canvas);
        Dispatcher.UIThread.RunJobs();
        ShowcaseCircuit.SetView(window, vm, DesignExtent(vm));
    }

    /// <summary>The components' bounding box with a margin, so the small MZI fills the canvas.</summary>
    private static (double X, double Y, double W, double H) DesignExtent(CAP.Avalonia.ViewModels.MainViewModel vm)
    {
        const double MarginMicrometers = 40;
        var components = vm.Canvas.Components;
        double minX = components.Min(c => c.X) - MarginMicrometers;
        double minY = components.Min(c => c.Y) - MarginMicrometers;
        double maxX = components.Max(c => c.X + c.Width) + MarginMicrometers;
        double maxY = components.Max(c => c.Y + c.Height) + MarginMicrometers;
        return (minX, minY, maxX - minX, maxY - minY);
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
        {
            ScreenshotArtifacts.SavePng(bitmap, path);
        }
    }
}
