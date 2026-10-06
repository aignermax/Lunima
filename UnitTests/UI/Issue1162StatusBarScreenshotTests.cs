using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.Views.Panels;
using Shouldly;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Status-bar overflow fix (#1162): the bar shows only the shortcuts relevant to the
/// current canvas mode, the full reference moved into a (?) flyout, and the process
/// badge keeps a fixed reserved column so it can never cover the hints again. The
/// structural facts are asserted unconditionally; PNGs + manifest.json land in
/// <c>artifacts/ui-screenshots/issue-1162/</c> when UI_SHOT_DIR is set.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1162StatusBarScreenshotTests
{
    private const int CaptureAttempts = 3;
    private const double MinBadgeColumnWidth = 150;
    private const double NarrowWindowWidth = 1280;
    private const double NarrowWindowHeight = 760;

    /// <summary>The badge reserves a fixed column; hints are mode-relevant and ellipsized.</summary>
    [AvaloniaFact]
    public void StatusBar_BadgeReservesFixedColumn_AndHintsAreModeRelevant()
    {
        var vm = ShowcaseCircuit.CreateStagedViewModel();
        vm.Home.IsHomeVisible = false;
        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            var badge = FindStatusBarBadge(window);
            badge.MinWidth.ShouldBeGreaterThanOrEqualTo(MinBadgeColumnWidth,
                "the process badge must reserve a fixed right-hand column (#1162)");

            var helpButton = window.GetVisualDescendants().OfType<HelpFlyoutButton>()
                .FirstOrDefault(h => h.HelpContent is ShortcutsHelpFlyout);
            helpButton.ShouldNotBeNull(
                "the full shortcut reference must be reachable from the status bar via a (?) flyout");

            var statusBar = badge.GetVisualAncestors().OfType<DockPanel>().First();
            statusBar.GetVisualDescendants().OfType<TextBlock>()
                .Any(t => t.Text?.Contains("Ctrl+Shift+G") == true && t.Text.Contains("F12"))
                .ShouldBeFalse("the old all-shortcuts string must not live in the status bar anymore");

            var hintBlock = FindHintTextBlock(statusBar, vm);
            hintBlock.TextTrimming.ShouldBe(TextTrimming.CharacterEllipsis,
                "an over-long hint must ellipsize instead of sliding under the badge");
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>The visible hint text follows the canvas interaction mode.</summary>
    [AvaloniaFact]
    public void StatusBar_HintTextFollowsTheCanvasMode()
    {
        var vm = ShowcaseCircuit.CreateStagedViewModel();
        vm.Home.IsHomeVisible = false;
        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            var statusBar = FindStatusBarBadge(window).GetVisualAncestors().OfType<DockPanel>().First();
            var hintBlock = FindHintTextBlock(statusBar, vm);

            vm.CanvasInteraction.CurrentMode = InteractionMode.Connect;
            Dispatcher.UIThread.RunJobs();
            hintBlock.Text.ShouldBe(vm.CanvasInteraction.ModeShortcutHints);
            hintBlock.Text.ShouldBe(LocalizationService.Instance.Translate("StatusBar.Hints.Connect"));

            vm.CanvasInteraction.CurrentMode = InteractionMode.Select;
            Dispatcher.UIThread.RunJobs();
            hintBlock.Text.ShouldBe(LocalizationService.Instance.Translate("StatusBar.Hints.Select"));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Captures the fixed status bar (en + de, two modes) and the shortcut flyout.</summary>
    [AvaloniaFact]
    public void CaptureFixedStatusBar()
    {
        // Opt-in like UiScreenshotTests: only runs when screenshots are explicitly requested.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UI_SHOT_DIR")))
            return;
        var outputDir = ResolveOutputDirectory();
        Directory.CreateDirectory(outputDir);
        foreach (var stale in Directory.GetFiles(outputDir, "*.png"))
            File.Delete(stale);

        // English, Select and Connect mode — the window is narrowed toward the width
        // of the original report so the reserved badge column is visibly doing its job.
        var vm = ShowcaseCircuit.CreateStagedViewModel();
        vm.Home.IsHomeVisible = false;
        var mainWindow = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            mainWindow.Width = NarrowWindowWidth;
            mainWindow.Height = NarrowWindowHeight;
            Dispatcher.UIThread.RunJobs();
            CaptureWithRetry(mainWindow, Path.Combine(outputDir, "01-statusbar-select-en.png"));

            vm.CanvasInteraction.CurrentMode = InteractionMode.Connect;
            Dispatcher.UIThread.RunJobs();
            CaptureWithRetry(mainWindow, Path.Combine(outputDir, "02-statusbar-connect-en.png"));
        }
        finally
        {
            mainWindow.Close();
            Dispatcher.UIThread.RunJobs();
        }

        // German — the locale of the original report screenshot ("Strg+Z/Y=Rückgängig/Wi…"
        // clipped under the "Prozess: Demo SOI 220nm" badge).
        LocalizationService.Instance.SetLanguage("de");
        try
        {
            var vmDe = ShowcaseCircuit.CreateStagedViewModel();
            vmDe.Home.IsHomeVisible = false;
            var deWindow = ShowcaseCircuit.BootMainWindow(vmDe);
            try
            {
                deWindow.Width = NarrowWindowWidth;
                deWindow.Height = NarrowWindowHeight;
                Dispatcher.UIThread.RunJobs();
                CaptureWithRetry(deWindow, Path.Combine(outputDir, "03-statusbar-select-de.png"));
            }
            finally
            {
                deWindow.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
        }

        // The (?) flyout carrying the full shortcut reference.
        var flyoutWindow = new Window
        {
            Width = 430,
            Height = 250,
            Background = Avalonia.Media.Brushes.Black,
            Content = new ShortcutsHelpFlyout(),
        };
        flyoutWindow.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            CaptureWithRetry(flyoutWindow, Path.Combine(outputDir, "04-shortcuts-flyout.png"));
        }
        finally
        {
            flyoutWindow.Close();
            Dispatcher.UIThread.RunJobs();
        }

        WriteManifest(outputDir);
        Directory.GetFiles(outputDir, "*.png").Length.ShouldBe(4);
    }

    private static Border FindStatusBarBadge(Window window) =>
        window.GetVisualDescendants().OfType<Border>()
            .First(b => b.Name == "ActiveProcessIndicator");

    /// <summary>The status-bar TextBlock showing the mode-relevant hints (bound to the VM).</summary>
    private static TextBlock FindHintTextBlock(DockPanel statusBar, CAP.Avalonia.ViewModels.MainViewModel vm) =>
        statusBar.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == vm.CanvasInteraction.ModeShortcutHints)
        ?? throw new ShouldAssertException("no status-bar TextBlock shows the mode-relevant hints");

    private static void WriteManifest(string outputDir)
    {
        const string manifest = """
        [
          {"file": "01-statusbar-select-en.png", "caption": "Fixed status bar (en, Select mode): only the mode-relevant hints remain in the bar; the process badge keeps its reserved right-hand column."},
          {"file": "02-statusbar-connect-en.png", "caption": "Same bar in Connect mode: the hints follow the canvas mode instead of listing every shortcut at once."},
          {"file": "03-statusbar-select-de.png", "caption": "German — the locale of the original report: the badge no longer covers the (now short) hint text."},
          {"file": "04-shortcuts-flyout.png", "caption": "The (?) flyout next to the hints carries the full keyboard-shortcut reference."}
        ]
        """;
        ScreenshotArtifacts.WriteText(Path.Combine(outputDir, "manifest.json"), manifest);
    }

    /// <summary>Repo-root <c>artifacts/ui-screenshots/issue-1162</c> (or <c>UI_SHOT_DIR/issue-1162</c>).</summary>
    private static string ResolveOutputDirectory()
    {
        var envDir = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (!string.IsNullOrEmpty(envDir))
            return Path.Combine(envDir, "issue-1162");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "artifacts", "ui-screenshots", "issue-1162");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-screenshots", "issue-1162");
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
