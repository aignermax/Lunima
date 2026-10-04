using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Views;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the Home screen's "Learn Lunima" section
/// (issue #1301): the four guided tours now form one numbered list in
/// learning order instead of four standalone buttons over three wrapped rows.
/// Captures the Home card in English and German (the language whose long
/// labels forced the wrapping) — PNGs + manifest.json land in
/// <c>docs/pr-media/issue-1301/</c> (only refreshed with CAP_UPDATE_PR_MEDIA=1;
/// ordinary runs write to a temp dir).
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1301LearnLunimaListScreenshotTests
{
    private const int CaptureAttempts = 5;
    private const int MinDistinctSampledColors = 10;
    private const int SampleGridSize = 64;

    private readonly string _outputDir;

    /// <summary>Resolves the PR-media output directory for this issue.</summary>
    public Issue1301LearnLunimaListScreenshotTests()
    {
        _outputDir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1301");
    }

    /// <summary>Captures the Home card with the numbered Learn list in English and German.</summary>
    [AvaloniaTheory]
    [InlineData("en", "home-learn-list-en.png")]
    [InlineData("de", "home-learn-list-de.png")]
    public void CaptureHomeLearnList(string languageCode, string fileName)
    {
        Directory.CreateDirectory(_outputDir);

        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(languageCode);
        try
        {
            var preferences = new UserPreferencesService(
                Path.Combine(Path.GetTempPath(), $"home-shot-prefs-{Guid.NewGuid():N}.json"));
            var home = new CAP.Avalonia.ViewModels.Home.HomeViewModel(
                new RecentProjectsService(preferences), preferences, new ExampleDesignsService());
            var window = new Window { Width = 900, Height = 720, Content = new HomeView { DataContext = home } };
            window.Show();
            try
            {
                PumpRenderLoop();
                SaveFrame(window, Path.Combine(_outputDir, fileName));
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }

        WriteManifest();
    }

    private static void SaveFrame(Window window, string path)
    {
        Bitmap? bitmap = null;
        for (var attempt = 0; attempt < CaptureAttempts; attempt++)
        {
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            var frame = window.CaptureRenderedFrame();
            if (frame == null)
                continue;
            (bitmap as IDisposable)?.Dispose();
            bitmap = frame;
        }

        bitmap.ShouldNotBeNull($"render miss for {Path.GetFileName(path)}");
        using (bitmap)
        {
            CountDistinctSampledColors(bitmap!).ShouldBeGreaterThan(MinDistinctSampledColors,
                "near-blank render — the capture would not document anything");
            ScreenshotArtifacts.SavePng(bitmap!, path).Length.ShouldBeGreaterThan(0);
        }
    }

    /// <summary>Advances the headless render timer so property changes actually paint before capture.</summary>
    private static void PumpRenderLoop()
    {
        for (int i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Samples a grid of pixels and counts distinct ARGB values (blank-frame guard).</summary>
    private static int CountDistinctSampledColors(Bitmap bitmap)
    {
        using var fb = ((WriteableBitmap)bitmap).Lock();
        int width = fb.Size.Width;
        int height = fb.Size.Height;
        if (width <= 0 || height <= 0)
            return 0;

        var pixels = new HashSet<int>();
        int stepX = Math.Max(1, width / SampleGridSize);
        int stepY = Math.Max(1, height / SampleGridSize);
        for (int y = 0; y < height; y += stepY)
        {
            var rowAddr = fb.Address + y * fb.RowBytes;
            for (int x = 0; x < width; x += stepX)
                pixels.Add(Marshal.ReadInt32(rowAddr, x * 4));
        }

        return pixels.Count;
    }

    private void WriteManifest()
    {
        var manifest = new[]
        {
            new
            {
                file = "home-learn-list-en.png",
                caption = "Home in English: the four guided tours are one numbered 'Learn Lunima' " +
                    "list in learning order — First steps, Watch it compute, Run a program, Connect two " +
                    "chiplets — each row one full-width entry with a one-line description. Placement: " +
                    "directly under New/Open on the Home card, because a first-time user must see the " +
                    "start-here path before examples and recents.",
            },
            new
            {
                file = "home-learn-list-de.png",
                caption = "Home in German: the long labels that previously wrapped the tour buttons " +
                    "across three rows now sit in the same single-column list — no wrapped button rows, " +
                    "nothing clipped.",
            },
        };
        ScreenshotArtifacts.WriteText(
            Path.Combine(_outputDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }
}
