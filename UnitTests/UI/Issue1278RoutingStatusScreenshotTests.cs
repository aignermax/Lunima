using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Services.Localization;
using Shouldly;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Routing status honesty (issue #1278): the status bar counts routed/total connections
/// with elapsed seconds during a full re-route and shows a Stop button only while
/// routing runs. The test drives the real MainWindow around the staged showcase chip:
/// it asserts the button's visibility follows <c>Canvas.IsRouting</c>, stops a real
/// routing pass through the button's command, and captures the status area mid-routing
/// and after the stop. PNGs + manifest.json land in <c>docs/pr-media/issue-1278/</c>
/// (only with <c>CAP_UPDATE_PR_MEDIA=1</c>; otherwise a temp dir — see
/// <see cref="ScreenshotArtifacts.ResolvePrMediaDirectory"/>).
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1278RoutingStatusScreenshotTests
{
    private const int CaptureAttempts = 3;

    private sealed record ManifestEntry(string File, string Caption);

    [AvaloniaFact]
    public async Task StatusBar_StopButtonFollowsIsRouting_AndStopsRealPass()
    {
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1278");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);
        var manifest = new List<ManifestEntry>();

        var (vm, window, _) = await ShowcaseCircuit.BootStagedMainWindowAsync();
        vm.Home.IsHomeVisible = false;
        Dispatcher.UIThread.RunJobs();
        try
        {
            var stopButton = FindStopButton(window);
            stopButton.Command.ShouldNotBeNull("the Stop button must be wired to a command");
            stopButton.IsVisible.ShouldBeFalse("no routing running — the Stop button stays hidden");

            // Mid-routing visual state: the same observable properties the orchestrator drives.
            var total = vm.Canvas.Connections.Count;
            total.ShouldBeGreaterThan(0);
            var routedSoFar = total / 2;
            vm.Canvas.IsRouting = true;
            vm.StatusText = string.Format(
                LocalizationService.Instance.Translate("Routing.Status.Progress"), routedSoFar, total, 1);
            Dispatcher.UIThread.RunJobs();
            stopButton.IsVisible.ShouldBeTrue("the Stop button appears while routing runs");
            Capture(window, Path.Combine(dir, "01-statusbar-mid-routing.png"),
                "Status bar mid-routing: 'Routing n/m connections · 1 s' progress text with the Stop button next to it.",
                manifest);

            // Stop a real routing pass through the button's command.
            vm.Canvas.IsRouting = false;
            Dispatcher.UIThread.RunJobs();
            var pass = vm.Canvas.RecalculateRoutesAsync();
            vm.Canvas.IsRouting.ShouldBeTrue("the pass start flips IsRouting synchronously");
            pass.IsCompleted.ShouldBeFalse("routing runs off the UI thread");
            stopButton.Command.Execute(null);
            while (!pass.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(5);
            }
            Dispatcher.UIThread.RunJobs();
            await pass;

            vm.Canvas.IsRouting.ShouldBeFalse();
            stopButton.IsVisible.ShouldBeFalse("the Stop button hides again once routing stopped");
            vm.StatusText.ShouldContain("/" + total);
            Capture(window, Path.Combine(dir, "02-statusbar-stopped.png"),
                "After Stop: the pass reports 'Routing stopped — n/m routed'; already-routed wires keep their routes.",
                manifest);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }

        ScreenshotArtifacts.WriteText(
            Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }));
        manifest.Count.ShouldBe(2);
    }

    /// <summary>The status-bar Stop button (found via its localized content).</summary>
    private static Button FindStopButton(Window window)
    {
        var statusBar = window.GetVisualDescendants().OfType<Border>()
            .First(b => b.Name == "ActiveProcessIndicator")
            .GetVisualAncestors().OfType<DockPanel>().First();
        return statusBar.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => Equals(b.Content, LocalizationService.Instance.Translate("Routing.Status.Stop")))
            ?? throw new ShouldAssertException("no Stop button in the status bar");
    }

    /// <summary>Captures the window, pumping the dispatcher and keeping the last good frame.</summary>
    private static void Capture(Window window, string path, string caption, List<ManifestEntry> manifest)
    {
        WriteableBitmap? bitmap = null;
        for (var attempt = 0; attempt < CaptureAttempts; attempt++)
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
        manifest.Add(new ManifestEntry(Path.GetFileName(path), caption));
    }
}
