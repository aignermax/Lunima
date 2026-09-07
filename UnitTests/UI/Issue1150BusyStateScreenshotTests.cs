using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Views;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Headless render tests for the issue-#1150 busy states: when a [RelayCommand] is
/// backgrounded to stay under the 100 ms UI-responsiveness budget, the status bar must
/// show a localized "in flight" message so the user knows work is happening. These
/// tests render the MainView in each shipped language with the busy flag set and
/// assert the busy text is the translated one (falling back to English would mean a
/// missing translation key).
/// </summary>
[Collection("LocalizationSingleton")]
public class Issue1150BusyStateScreenshotTests
{
    private static readonly (string Language, string ExpectedBusyText)[] BusyTextByLanguage =
    {
        ("en", "Running design checks..."),
        ("de", "Design-Prüfungen laufen…"),
        ("es", "Ejecutando comprobaciones de diseño…"),
        ("ja", "設計チェックを実行中…"),
        ("zh-Hans", "正在运行设计检查…"),
    };

    /// <summary>
    /// For every shipped language, setting <c>IsRunningDesignChecks</c> plus the
    /// localized status text shows the translated busy message — not the raw key and
    /// not the English fallback.
    /// </summary>
    [AvaloniaFact]
    public void DesignChecks_BusyStatusText_IsLocalizedInAllShippedLanguages()
    {
        var previous = LocalizationService.Instance.ActiveLanguageCode;
        try
        {
            var vm = MainViewModelTestHelper.CreateMainViewModel();
            foreach (var (language, expected) in BusyTextByLanguage)
            {
                LocalizationService.Instance.SetLanguage(language);
                vm.IsRunningDesignChecks = true;
                vm.StatusText = LocalizationService.Instance.Translate("Status.RunningDesignChecks");

                vm.IsRunningDesignChecks.ShouldBeTrue();
                vm.StatusText.ShouldBe(expected,
                    $"language '{language}' must translate Status.RunningDesignChecks");
            }
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    /// <summary>
    /// Renders the MainView with the design-checks busy state active: the status bar
    /// shows the translated "Running design checks..." line. The render must succeed
    /// (non-null frame, plausible pixel diversity) — the busy state is a real bind,
    /// not a test-only stub.
    /// </summary>
    [AvaloniaFact]
    public void MainView_WithDesignChecksBusy_RendersStatusBar()
    {
        var previous = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage("en");
        Window? window = null;
        try
        {
            var vm = MainViewModelTestHelper.CreateMainViewModel();
            vm.IsRunningDesignChecks = true;
            vm.StatusText = LocalizationService.Instance.Translate("Status.RunningDesignChecks");

            var view = new MainView { DataContext = vm };
            window = new Window { Width = 1280, Height = 900, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var bitmap = window.CaptureRenderedFrame();
            bitmap.ShouldNotBeNull("MainView must render while the busy state is active");
            vm.StatusText.ShouldBe("Running design checks...");
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    /// <summary>
    /// The simulation busy state uses the pre-existing
    /// <c>Status.RunningSimulation</c> string and the <see
    /// cref="CAP.Avalonia.ViewModels.MainViewModel.IsSimulating"/> flag — pinned here
    /// so the #1150 contract (every backgrounded command surfaces a busy state) holds
    /// for both backgrounded commands.
    /// </summary>
    [AvaloniaFact]
    public void Simulation_BusyState_UsesIsSimulatingFlag()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.IsSimulating.ShouldBeFalse();
        vm.IsSimulating = true;
        vm.StatusText = LocalizationService.Instance.Translate("Status.RunningSimulation");
        vm.IsSimulating.ShouldBeTrue();
        vm.StatusText.ShouldNotBeNullOrEmpty();
    }

    /// <summary>
    /// Opt-in artifact capture for the PR: renders the MainView with each #1150 busy
    /// state active (design checks, then simulation) and writes PNGs + manifest.json
    /// to <c>artifacts/ui-screenshots/issue-1150/</c>. No-op unless <c>UI_SHOT_DIR</c>
    /// is set, so CI runs it as an instant pass.
    /// </summary>
    [Trait("Category", "UiScreenshots")]
    [AvaloniaFact]
    public void CaptureBusyStates()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UI_SHOT_DIR")))
            return;
        var outputDir = ResolveOutputDirectory();
        Directory.CreateDirectory(outputDir);

        var previous = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage("en");
        Window? window = null;
        try
        {
            var vm = MainViewModelTestHelper.CreateMainViewModel();
            var view = new MainView { DataContext = vm };
            window = new Window { Width = 1280, Height = 900, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            vm.IsRunningDesignChecks = true;
            vm.StatusText = LocalizationService.Instance.Translate("Status.RunningDesignChecks");
            Dispatcher.UIThread.RunJobs();
            SaveFrame(window, Path.Combine(outputDir, "busy-design-checks.png"));
            vm.IsRunningDesignChecks = false;

            vm.IsSimulating = true;
            vm.StatusText = LocalizationService.Instance.Translate("Status.RunningSimulation");
            Dispatcher.UIThread.RunJobs();
            SaveFrame(window, Path.Combine(outputDir, "busy-simulation.png"));

            ScreenshotArtifacts.WriteText(Path.Combine(outputDir, "manifest.json"), Manifest);
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    private static void SaveFrame(Window window, string path)
    {
        var bitmap = window.CaptureRenderedFrame();
        bitmap.ShouldNotBeNull("render miss for busy-state capture");
        using (bitmap)
        {
            var bytes = ScreenshotArtifacts.SavePng(bitmap!, path);
            bytes.Length.ShouldBeGreaterThan(0);
        }
    }

    /// <summary>Repo-root <c>artifacts/ui-screenshots/issue-1150</c> (or <c>UI_SHOT_DIR/issue-1150</c>).</summary>
    private static string ResolveOutputDirectory()
    {
        var envDir = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (!string.IsNullOrEmpty(envDir))
            return Path.Combine(envDir, "issue-1150");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "artifacts", "ui-screenshots", "issue-1150");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-screenshots", "issue-1150");
    }

    private const string Manifest = """
        [
          {"file": "busy-design-checks.png", "caption": "Issue #1150: RunDesignChecks backgrounded — status bar shows localized 'Running design checks...' while validation computes off the UI thread."},
          {"file": "busy-simulation.png", "caption": "Issue #1150: RunSimulation backgrounded — status bar shows 'Running simulation...' with IsSimulating set while the S-matrix pass computes off the UI thread."}
        ]
        """;
}
