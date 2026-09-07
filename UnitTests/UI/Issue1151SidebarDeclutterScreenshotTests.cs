using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Views;
using CAP.Avalonia.Views.Panels;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Sidebar de-clutter regression + visual documentation: the right sidebar hosts
/// selection properties only, while Sweep / Design Checks / Netlist live as
/// analysis-dock tabs and the AI Assistant in its own tool window. The structural
/// facts are asserted unconditionally; PNGs + manifest.json land in
/// <c>artifacts/ui-screenshots/issue-1151/</c> when UI_SHOT_DIR is set.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1151SidebarDeclutterScreenshotTests
{
    private const int DockWindowWidth = 1000;
    private const int DockWindowHeight = 560;
    private const int CaptureAttempts = 3;
    private const int SweepTabIndex = 5;
    private const int ChecksTabIndex = 6;
    private const int NetlistTabIndex = 7;

    /// <summary>The analysis dock hosts the re-homed Sweep, Checks and Netlist tabs.</summary>
    [AvaloniaFact]
    public void AnalysisDock_HostsSweepChecksAndNetlistTabs()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.BottomPanel.Analysis.IsVisible = true;
        var dock = new AnalysisDockPanel { DataContext = vm };
        var window = new Window { Width = DockWindowWidth, Height = DockWindowHeight, Content = dock };
        window.Show();
        try
        {
            AssertVisibleTab<ParameterSweepPanel>(dock, vm, SweepTabIndex);
            AssertVisibleTab<DesignChecksPanel>(dock, vm, ChecksTabIndex);
            AssertVisibleTab<NetlistPanel>(dock, vm, NetlistTabIndex);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>The right sidebar contains selection-driven panels only.</summary>
    [AvaloniaFact]
    public void RightSidebar_ContainsOnlySelectionProperties()
    {
        var vm = ShowcaseCircuit.CreateStagedViewModel();
        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            var rightPanel = window.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b => b.Name == "RightPanelBorder");
            rightPanel.ShouldNotBeNull();
            rightPanel.GetVisualDescendants().OfType<AiAssistantPanel>().ShouldBeEmpty();
            rightPanel.GetVisualDescendants().OfType<DesignChecksPanel>().ShouldBeEmpty();
            rightPanel.GetVisualDescendants().OfType<NetlistPanel>().ShouldBeEmpty();
            rightPanel.GetVisualDescendants().OfType<ParameterSweepPanel>().ShouldBeEmpty();
            rightPanel.GetVisualDescendants().OfType<SelectedComponentPropertiesPanel>().ShouldHaveSingleItem();
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Captures the de-cluttered sidebar, the new dock tabs and the AI window.</summary>
    [AvaloniaFact]
    public void CaptureDeclutteredSurfaces()
    {
        // Opt-in like UiScreenshotTests: only runs when screenshots are explicitly requested.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UI_SHOT_DIR")))
            return;
        var outputDir = ResolveOutputDirectory();
        Directory.CreateDirectory(outputDir);
        foreach (var stale in Directory.GetFiles(outputDir, "*.png"))
            File.Delete(stale);

        var vm = ShowcaseCircuit.CreateStagedViewModel();
        // Dismiss the Home overlay so the capture shows the de-cluttered editor itself.
        vm.Home.IsHomeVisible = false;

        // 1: full editor — right sidebar shows only the selection-properties empty state.
        var mainWindow = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            CaptureWithRetry(mainWindow, Path.Combine(outputDir, "01-main-window.png"));
        }
        finally
        {
            mainWindow.Close();
            Dispatcher.UIThread.RunJobs();
        }

        // 2-4: the analysis dock on each re-homed tab.
        vm.BottomPanel.Analysis.IsVisible = true;
        vm.BottomPanel.Analysis.SetDockHeight(380);
        var dock = new AnalysisDockPanel { DataContext = vm };
        var dockWindow = new Window { Width = DockWindowWidth, Height = DockWindowHeight, Content = dock };
        dockWindow.Show();
        try
        {
            vm.BottomPanel.Analysis.SelectedTabIndex = SweepTabIndex;
            Dispatcher.UIThread.RunJobs();
            CaptureWithRetry(dockWindow, Path.Combine(outputDir, "02-dock-sweep-tab.png"));

            vm.BottomPanel.Analysis.SelectedTabIndex = ChecksTabIndex;
            Dispatcher.UIThread.RunJobs();
            CaptureWithRetry(dockWindow, Path.Combine(outputDir, "03-dock-checks-tab.png"));

            vm.BottomPanel.Analysis.SelectedTabIndex = NetlistTabIndex;
            Dispatcher.UIThread.RunJobs();
            CaptureWithRetry(dockWindow, Path.Combine(outputDir, "04-dock-netlist-tab.png"));
        }
        finally
        {
            dockWindow.Close();
            Dispatcher.UIThread.RunJobs();
        }

        // 5: the AI Design Assistant in its own tool window.
        var aiWindow = new AiAssistantWindow { DataContext = vm };
        aiWindow.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            CaptureWithRetry(aiWindow, Path.Combine(outputDir, "05-ai-assistant-window.png"));
        }
        finally
        {
            aiWindow.Close();
            Dispatcher.UIThread.RunJobs();
        }

        WriteManifest(outputDir);
        Directory.GetFiles(outputDir, "*.png").Length.ShouldBe(5);
    }

    private static void AssertVisibleTab<TPanel>(AnalysisDockPanel dock,
        CAP.Avalonia.ViewModels.MainViewModel vm, int tabIndex) where TPanel : Control
    {
        vm.BottomPanel.Analysis.SelectedTabIndex = tabIndex;
        Dispatcher.UIThread.RunJobs();
        dock.GetVisualDescendants().OfType<TPanel>()
            .Any(p => p.IsEffectivelyVisible)
            .ShouldBeTrue($"{typeof(TPanel).Name} is not the visible analysis tab at index {tabIndex}.");
    }

    private static void WriteManifest(string outputDir)
    {
        const string manifest = """
        [
          {"file": "01-main-window.png", "caption": "Main window after the de-clutter: the right sidebar shows only selection properties (empty-state hint when nothing is selected)."},
          {"file": "02-dock-sweep-tab.png", "caption": "Parameter Sweep re-homed as an analysis-dock tab, with a hint to select a slider component when none is selected."},
          {"file": "03-dock-checks-tab.png", "caption": "Design Checks re-homed as an analysis-dock tab — run checks and step through issues like an IDE problems panel."},
          {"file": "04-dock-netlist-tab.png", "caption": "Netlist (gdsfactory YAML) re-homed as an analysis-dock tab with generate/save/copy actions."},
          {"file": "05-ai-assistant-window.png", "caption": "AI Design Assistant in its own non-modal tool window, opened from the new 🤖 toolbar button."}
        ]
        """;
        ScreenshotArtifacts.WriteText(Path.Combine(outputDir, "manifest.json"), manifest);
    }

    /// <summary>Repo-root <c>artifacts/ui-screenshots/issue-1151</c> (or <c>UI_SHOT_DIR/issue-1151</c>).</summary>
    private static string ResolveOutputDirectory()
    {
        var envDir = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (!string.IsNullOrEmpty(envDir))
            return Path.Combine(envDir, "issue-1151");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "artifacts", "ui-screenshots", "issue-1151");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-screenshots", "issue-1151");
    }

    /// <summary>Captures the window, pumping the dispatcher and keeping the last good frame.</summary>
    private static void CaptureWithRetry(Window window, string path)
    {
        WriteableBitmap? bitmap = null;
        for (int attempt = 0; attempt < CaptureAttempts; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            var frame = window.CaptureRenderedFrame();
            if (frame == null)
                continue;
            bitmap?.Dispose();
            bitmap = frame;
        }

        bitmap.ShouldNotBeNull($"CaptureRenderedFrame stayed null after {CaptureAttempts} attempts for {path}");
        using (bitmap)
        {
            ScreenshotArtifacts.SavePng(bitmap, path);
        }
    }
}
