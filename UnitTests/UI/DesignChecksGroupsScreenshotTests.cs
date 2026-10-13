using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.Views.Panels;
using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using Xunit;
using static UnitTests.Analysis.Hierarchy.NestedDesignBuilder;
using Component = CAP_Core.Components.Core.Component;

namespace UnitTests.UI;

/// <summary>
/// Design checks on nested groups, headless: the Checks tab lists a blocked frozen path two
/// groups deep with its group path while the unconnected-pin hints stay hidden behind the
/// "Show unconnected pins" toggle; the canvas marks the group holding the broken path with
/// a red dashed outline and a "!" badge, and draws the blocked path itself red. PNGs +
/// manifest land in <c>docs/pr-media/design-checks-groups/</c> with
/// <c>CAP_UPDATE_PR_MEDIA=1</c> (a temp dir otherwise).
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class DesignChecksGroupsScreenshotTests
{
    private const int CaptureAttempts = 3;
    private const double CanvasZoom = 1.0;

    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>RAM ⊃ CELL1 ⊃ { B, C } with a blocked B.out → C.in fallback, plus a clean sibling cell.</summary>
    private static ComponentGroup BuildRam()
    {
        var b = Leaf("B", 160, 120);
        var c = Leaf("C", 360, 160);
        var cell1 = Group("CELL1", b, c);
        var blocked = Frozen(cell1, Pin(b, "out"), Pin(c, "in"), (260, 130), (360, 170));
        blocked.Path.IsBlockedFallback = true;

        var d = Leaf("D", 160, 300);
        var e = Leaf("E", 360, 300);
        var cell0 = Group("CELL0", d, e);
        Frozen(cell0, Pin(d, "out"), Pin(e, "in"), (260, 310), (360, 310));
        return Group("RAM", cell1, cell0);
    }

    [AvaloniaFact]
    public void CaptureDesignChecksOnNestedGroups()
    {
        LocalizationService.Instance.SetLanguage("en");
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("design-checks-groups");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);
        var manifest = new List<ManifestEntry>();

        var canvas = new DesignCanvasViewModel();
        var ram = BuildRam();
        canvas.Components.Add(new ComponentViewModel(ram));
        var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        vm.RightPanel.DesignValidation.RunValidation(
            canvas.ConnectionManager.Connections, new[] { ram }, new Component[] { ram }, processLockActive: false);

        CapturePanel(vm, Path.Combine(dir, "01-checks-nested-blocked-hints-hidden.png"), manifest,
            "Checks tab: the blocked frozen path two groups deep is listed as 'Blocked path in group RAM/CELL1'; "
            + "the unconnected-pin hints are hidden behind the toggle (count in brackets) and not counted as issues.",
            expectText: "Blocked path in group 'RAM/CELL1'");

        vm.RightPanel.DesignValidation.ShowUnconnectedPins = true;
        CapturePanel(vm, Path.Combine(dir, "02-checks-hints-shown.png"), manifest,
            "Same run with 'Show unconnected pins' on: the open pins appear after the real problems.",
            expectText: "Blocked path in group 'RAM/CELL1'");

        CaptureCanvas(vm, canvas, Path.Combine(dir, "03-canvas-group-broken-path-marker.png"), manifest,
            "Canvas from the top level: RAM and CELL1 carry the red dashed outline and '!' badge because a "
            + "frozen path inside is blocked; the blocked path itself draws red. CELL0 is clean.");

        ScreenshotArtifacts.WriteText(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }));
        manifest.Count.ShouldBe(3);
    }

    private static void CapturePanel(MainViewModel vm, string path, List<ManifestEntry> manifest,
        string caption, string expectText)
    {
        var window = new Window { Width = 620, Height = 320, Content = new DesignChecksPanel { DataContext = vm } };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
            texts.ShouldContain(t => t.Contains(expectText), string.Join(" | ", texts));
            Save(window, path);
            manifest.Add(new ManifestEntry(Path.GetFileName(path), caption));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void CaptureCanvas(MainViewModel vm, DesignCanvasViewModel canvas, string path,
        List<ManifestEntry> manifest, string caption)
    {
        var designCanvas = new DesignCanvas { ViewModel = canvas, MainViewModel = vm, Zoom = CanvasZoom };
        var window = new Window { Width = 900, Height = 760, Content = designCanvas };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            designCanvas.InvalidateVisual();
            Save(window, path);
            manifest.Add(new ManifestEntry(Path.GetFileName(path), caption));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void Save(Window window, string path)
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
    }
}
