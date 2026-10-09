using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Views.Panels;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using Shouldly;
using UnitTests.Helpers;
using Xunit;
using Component = CAP_Core.Components.Core.Component;

namespace UnitTests.UI;

/// <summary>
/// Issue #1308: the component/group footprint overlap finding surfaces in the existing
/// Design Checks tab — no new UI surface, the finding is a
/// <see cref="CAP_Core.Analysis.DesignIssue"/> like every other DRC-lite rule. The
/// structural fact (the warning text naming both items is visible in the tab) is
/// asserted unconditionally; PNGs + manifest.json land in
/// <c>docs/pr-media/issue-1308/</c> (only with <c>CAP_UPDATE_PR_MEDIA=1</c>;
/// otherwise a temp dir — see <see cref="ScreenshotArtifacts.ResolvePrMediaDirectory"/>).
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1308FootprintOverlapScreenshotTests
{
    private const int CaptureAttempts = 3;
    private const double PanelWidth = 560;
    private const double PanelHeight = 300;

    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>The tab shows the footprint-overlap warning naming both items.</summary>
    [AvaloniaFact]
    public void DesignChecksTab_ShowsFootprintOverlapWarning()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        RunValidation(vm, overlapping: true);

        var window = new Window
        {
            Width = PanelWidth,
            Height = PanelHeight,
            Content = new DesignChecksPanel { DataContext = vm },
        };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var warning = window.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => t.Text?.Contains("Overlapping component footprints") == true);
            warning.ShouldNotBeNull("the Design Checks tab must show the footprint-overlap warning");
            warning.Text.ShouldContain("AND0");
            warning.Text.ShouldContain("NOT0");
            warning.Text.ShouldContain("µm");
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Captures the Checks tab: clean pair vs. the shoved-together overlap.</summary>
    [AvaloniaFact]
    public void CaptureDesignChecksTab()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1308");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);
        var manifest = new List<ManifestEntry>();

        var cleanVm = MainViewModelTestHelper.CreateMainViewModel();
        RunValidation(cleanVm, overlapping: false);
        Capture(cleanVm, Path.Combine(dir, "01-design-checks-clean.png"),
            "Design Checks tab with the AND0/NOT0 slices standing clear: no footprint-overlap finding.",
            manifest);

        var overlapVm = MainViewModelTestHelper.CreateMainViewModel();
        RunValidation(overlapVm, overlapping: true);
        Capture(overlapVm, Path.Combine(dir, "02-design-checks-footprint-overlap.png"),
            "Same tab after NOT0 is shoved 120 µm into AND0's footprint: the DRC-lite warning names both items and the overlap size.",
            manifest);

        ScreenshotArtifacts.WriteText(
            Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }));
        manifest.Count.ShouldBe(2);
    }

    /// <summary>
    /// Runs the validation the way the Design Checks button does, over two gate slices
    /// (AND0 above NOT0, as in the Logic Unit 4-bit example) — either standing clear
    /// or with NOT0 shoved 120 µm up into AND0's footprint (the #1304 mutation).
    /// </summary>
    private static void RunValidation(
        CAP.Avalonia.ViewModels.MainViewModel vm, bool overlapping)
    {
        var and0 = CreateSlice("AND0", x: 0, y: 0);
        var not0 = CreateSlice("NOT0", x: 0, y: overlapping ? 80 : 200);

        vm.RightPanel.DesignValidation.RunValidation(
            Array.Empty<WaveguideConnection>(),
            allComponents: new Component[] { and0, not0 },
            processLockActive: false);
    }

    private static Component CreateSlice(string identifier, double x, double y)
    {
        var component = TestComponentFactory.CreateStraightWaveGuide();
        component.Identifier = identifier;
        component.PhysicalX = x;
        component.PhysicalY = y;
        component.WidthMicrometers = 150;
        component.HeightMicrometers = 100;
        return component;
    }

    private static void Capture(
        CAP.Avalonia.ViewModels.MainViewModel vm, string path, string caption,
        List<ManifestEntry> manifest)
    {
        var window = new Window
        {
            Width = PanelWidth,
            Height = PanelHeight,
            Content = new DesignChecksPanel { DataContext = vm },
        };
        window.Show();
        try
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
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
