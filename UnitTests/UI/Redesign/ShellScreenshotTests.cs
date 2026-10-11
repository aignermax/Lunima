using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.Views;
using CAP_Core.Components.Process;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using UnitTests.Integration.RamScale;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI.Redesign;

/// <summary>
/// Visual documentation of the redesigned main-window shell (UI redesign phase 1): toolbar,
/// left icon rail with one full-height pane, properties panel, docks and the fixed status
/// bar. Opt-in like the other screenshot tests — PNGs land in <c>UI_SHOT_DIR/ui-redesign/</c>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class ShellScreenshotTests
{
    private const string ExampleFileName = "EBeam Mach-Zehnder Interferometer.lun";
    private const int CaptureAttempts = 3;
    private const double DockHeight = 240;
    private const int BlockedConnectionsToFake = 2;

    /// <summary>Empty editor, example design with a selection, each rail section, blocked chip.</summary>
    [AvaloniaFact]
    public async Task CaptureShell()
    {
        var root = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (string.IsNullOrEmpty(root))
            return;
        var outputDir = Path.Combine(root, "ui-redesign");
        Directory.CreateDirectory(outputDir);
        foreach (var stale in Directory.GetFiles(outputDir, "*.png"))
            File.Delete(stale);

        CaptureEmptyEditor(outputDir);
        await CaptureExampleDesign(outputDir);

        Directory.GetFiles(outputDir, "*.png").Length.ShouldBeGreaterThanOrEqualTo(7);
    }

    private static void CaptureEmptyEditor(string outputDir)
    {
        var vm = ShowcaseCircuit.CreateStagedViewModel();
        vm.Home.IsHomeVisible = false;
        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            Capture(window, Path.Combine(outputDir, "01-main-empty.png"));
            vm.LeftRail.ShowHierarchyCommand.Execute(null);
            Capture(window, Path.Combine(outputDir, "02-hierarchy-empty.png"));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task CaptureExampleDesign(string outputDir)
    {
        var vm = await LoadExampleAsync();
        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            vm.Home.IsHomeVisible = false;
            Dispatcher.UIThread.RunJobs();
            FitDesign(window, vm);
            Capture(window, Path.Combine(outputDir, "03-example-loaded.png"));

            SelectLargestComponent(vm);
            Capture(window, Path.Combine(outputDir, "04-example-component-selected.png"));

            vm.LeftRail.ShowHierarchyCommand.Execute(null);
            Capture(window, Path.Combine(outputDir, "05-rail-hierarchy.png"));
            vm.LeftRail.ShowPdksCommand.Execute(null);
            Capture(window, Path.Combine(outputDir, "06-rail-pdks.png"));
            vm.LeftRail.ShowLibraryCommand.Execute(null);

            FakeBlockedConnections(vm);
            vm.BlockedConnections.SelectNextBlockedCommand.Execute(null);
            Capture(window, Path.Combine(outputDir, "07-statusbar-blocked.png"));

            vm.BottomPanel.Analysis.IsVisible = true;
            vm.BottomPanel.Analysis.SetDockHeight(DockHeight);
            vm.BottomPanel.Analysis.SelectedTabIndex = 2;
            vm.BottomPanel.ErrorConsole.IsVisible = true;
            Capture(window, Path.Combine(outputDir, "08-docks-open.png"));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Loads the shipped MZI example through the real load path.</summary>
    private static async Task<MainViewModel> LoadExampleAsync()
    {
        var canvas = new CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel();
        var fileOps = Ram4x4FeasibilityTests.CreateFileOperations(canvas);
        fileOps.ApplyChipSizeAfterLoad = (w, h) => Ram4x4FeasibilityTests.ApplyChipSize(canvas, w, h);
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFileName);
        (await fileOps.LoadDesignFromPathAsync(path)).ShouldBeTrue($"'{ExampleFileName}' must load");
        await fileOps.PostLoadRouting;

        var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        vm.LeftPanel.HierarchyPanel.RebuildTree();
        vm.FileOperations.SetActiveProcess(ActiveProcessSelection.Playground(), markDirty: false);
        return vm;
    }

    /// <summary>Clicks the largest component, like a user would, so every selection consumer updates.</summary>
    private static void SelectLargestComponent(MainViewModel vm)
    {
        var target = vm.Canvas.Components.OrderByDescending(c => c.Width * c.Height).First();
        vm.CanvasClicked(target.X + target.Width / 2, target.Y + target.Height / 2);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Flags connections as blocked fallbacks, the state the router leaves after giving up.</summary>
    private static void FakeBlockedConnections(MainViewModel vm)
    {
        foreach (var connection in vm.Canvas.Connections
                     .Where(c => c.Connection.RoutedPath != null)
                     .Take(BlockedConnectionsToFake))
        {
            connection.Connection.RoutedPath!.IsBlockedFallback = true;
            connection.NotifyPathChanged();
        }
        vm.BlockedConnections.BlockedCount.ShouldBeGreaterThan(0);
    }

    private static void FitDesign(MainWindow window, MainViewModel vm)
    {
        var components = vm.Canvas.Components;
        if (components.Count == 0)
            return;
        double minX = components.Min(c => c.X);
        double minY = components.Min(c => c.Y);
        double maxX = components.Max(c => c.X + c.Width);
        double maxY = components.Max(c => c.Y + c.Height);
        ShowcaseCircuit.SetView(window, vm, (minX, minY, maxX - minX, maxY - minY));
    }

    /// <summary>Pumps layout + render, then captures the window, keeping the last good frame.</summary>
    private static void Capture(MainWindow window, string path)
    {
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
