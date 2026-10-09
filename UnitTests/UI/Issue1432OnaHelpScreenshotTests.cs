using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Controls.HelpAnimations;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Views.Dialogs;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the ONA help flyout (#1432): renders the exact panel that
/// <see cref="OnaAnalyzerWindow"/> hosts inside its <see cref="HelpFlyoutButton"/>,
/// frozen at three animation phases (laser early / on a resonance / near the end) in
/// English and in German. PNGs + manifest.json land in
/// <c>docs/pr-media/issue-1432/</c> (only with <c>CAP_UPDATE_PR_MEDIA=1</c>).
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1432OnaHelpScreenshotTests
{
    private const int CaptureAttempts = 3;
    private const double HostWidth = 440;
    private const double HostHeight = 380;

    // Chosen so the three phases are visually distinct: marker at the left with the
    // ring dim, marker dead-centre on the middle resonance with the ring lit, and the
    // marker near the right edge past all resonances.
    private static readonly (double Phase, string Label)[] Phases =
    {
        (0.10, "01-laser-early"),
        (0.55, "02-on-resonance"),
        (0.95, "03-sweep-end"),
    };

    [AvaloniaFact]
    public void CaptureOnaHelpFlyoutInEnglishAndGerman()
    {
        var outputDir = ResolveOutputDirectory();
        Directory.CreateDirectory(outputDir);
        foreach (var stale in Directory.GetFiles(outputDir, "*.png"))
            File.Delete(stale);

        var manifest = new List<object>();

        try
        {
            foreach (var language in new[] { "en", "de" })
            {
                LocalizationService.Instance.SetLanguage(language);
                var bytesPerPhase = CaptureAllPhases(outputDir, language, manifest);

                // Within one language, the three phases must produce different frames —
                // identical bytes would mean the laser marker did not actually move.
                for (int i = 1; i < bytesPerPhase.Count; i++)
                {
                    bytesPerPhase[i].SequenceEqual(bytesPerPhase[0]).ShouldBeFalse(
                        $"phase {i} is pixel-identical to phase 0 in '{language}' — " +
                        "the animation did not advance");
                }
            }

            // The German captures must differ from the English ones — otherwise the
            // {loc:Localize} bindings did not pick up the language switch.
            LocalizationService.Instance.SetLanguage("en");
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
        }

        ScreenshotArtifacts.WriteText(
            Path.Combine(outputDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Hosts the exact HelpContent of <see cref="OnaAnalyzerWindow"/>'s help button in a
    /// headless window, pins the animation at each phase, captures and returns the PNGs.
    /// </summary>
    private static List<byte[]> CaptureAllPhases(
        string outputDir,
        string language,
        List<object> manifest)
    {
        var onaWindow = new OnaAnalyzerWindow();
        onaWindow.Show();
        Dispatcher.UIThread.RunJobs();

        var helpButton = onaWindow.GetVisualDescendants().OfType<HelpFlyoutButton>().FirstOrDefault();
        helpButton.ShouldNotBeNull("the ONA window must host a HelpFlyoutButton next to its subtitle");

        var panel = helpButton.HelpContent as Control;
        panel.ShouldNotBeNull("the help flyout content must be a control tree");

        // Detach from the flyout so the host window can re-parent it.
        helpButton.HelpContent = null;
        onaWindow.Close();
        Dispatcher.UIThread.RunJobs();

        var animation = panel!.GetVisualDescendants().OfType<OnaSweepAnimation>().FirstOrDefault();
        animation.ShouldNotBeNull("the help flyout must host the OnaSweepAnimation");
        animation!.AutoAdvance = false;

        // Wrap the panel in the same chrome the real flyout uses (dark border, padding,
        // bold title) so the captures reflect what the user actually sees on "?"-click.
        var flyoutTitle = new TextBlock
        {
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontSize = 13,
            Foreground = Avalonia.Media.Brushes.White,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        flyoutTitle.Bind(TextBlock.TextProperty,
            new Avalonia.Data.Binding("Title") { Source = helpButton });

        var contentStack = new StackPanel { Margin = new Avalonia.Thickness(14), Spacing = 8 };
        contentStack.Children.Add(flyoutTitle);
        contentStack.Children.Add(panel);

        var chrome = new Border
        {
            Background = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(
                Avalonia.Media.Color.FromRgb(0x1a, 0x1a, 0x1a)),
            BorderBrush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(
                Avalonia.Media.Color.FromRgb(0x3d, 0x3d, 0x3d)),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(6),
            Child = new ScrollViewer { Content = contentStack },
        };

        var host = new Window
        {
            Width = HostWidth,
            Height = HostHeight,
            Background = Avalonia.Media.Brushes.Black,
            Content = chrome,
        };
        host.Show();
        Dispatcher.UIThread.RunJobs();

        var bytesPerPhase = new List<byte[]>();
        try
        {
            foreach (var (phase, label) in Phases)
            {
                animation.Progress = phase;
                Dispatcher.UIThread.RunJobs();

                var fileName = $"{language}-{label}.png";
                var path = Path.Combine(outputDir, fileName);
                bytesPerPhase.Add(CaptureWithRetry(host, path));
                manifest.Add(new
                {
                    file = fileName,
                    caption = $"ONA help flyout ({language}) at animation phase {phase:F2}: "
                        + (phase == Phases[1].Phase
                            ? "the laser marker sits on the middle resonance — the small ring next to the plot is lit."
                            : "the laser marker is off-resonance — the small ring stays dim."),
                });
            }
        }
        finally
        {
            host.Close();
            Dispatcher.UIThread.RunJobs();
        }

        return bytesPerPhase;
    }

    /// <summary>
    /// Captures the window, pumping the dispatcher before every attempt and keeping the
    /// last successful frame (headless rendering can miss frames — same pattern as
    /// <see cref="Issue819LaserSpectrumScreenshotTests"/>).
    /// </summary>
    private static byte[] CaptureWithRetry(Window window, string path)
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
            return ScreenshotArtifacts.SavePng(bitmap, path);
        }
    }

    /// <summary>
    /// <c>docs/pr-media/issue-1432</c> when <c>CAP_UPDATE_PR_MEDIA=1</c>, otherwise a temp
    /// directory — a plain test run must not rewrite the committed PR media.
    /// </summary>
    private static string ResolveOutputDirectory() =>
        ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1432");
}
