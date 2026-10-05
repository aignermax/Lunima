using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Views.Panels;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Issue #1219 (rung 6): the cross-chiplet edge-coupler warning surfaces in the existing
/// Design Checks tab — no new UI surface, the finding is a <see cref="CAP_Core.Analysis.DesignIssue"/>
/// like every other DRC-lite rule. The structural fact (the warning text is visible in the
/// tab) is asserted unconditionally; PNGs + manifest.json land in
/// <c>artifacts/ui-screenshots/issue-1219/</c> when UI_SHOT_DIR is set.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1219ChipletInterfaceScreenshotTests
{
    private const int CaptureAttempts = 3;
    private const double PanelWidth = 560;
    private const double PanelHeight = 300;

    /// <summary>The tab shows the lateral-offset warning naming both chiplets.</summary>
    [AvaloniaFact]
    public void DesignChecksTab_ShowsLateralOffsetWarning()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var design = ChipletEdgeCouplerJourneyDesign.BuildComposed();
        design.ChipletB.MoveGroup(0, 2.0);
        RunValidation(vm, design);

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
                .FirstOrDefault(t => t.Text?.Contains("laterally offset") == true);
            warning.ShouldNotBeNull("the Design Checks tab must show the chiplet-interface warning");
            warning.Text.ShouldContain(ChipletEdgeCouplerJourneyDesign.ChipletAName);
            warning.Text.ShouldContain(ChipletEdgeCouplerJourneyDesign.ChipletBName);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Captures the Checks tab: clean journey vs. the 2 µm lateral-offset warning.</summary>
    [AvaloniaFact]
    public void CaptureDesignChecksTab()
    {
        // Opt-in like UiScreenshotTests: only runs when screenshots are explicitly requested.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UI_SHOT_DIR")))
            return;
        var outputDir = ResolveOutputDirectory();
        Directory.CreateDirectory(outputDir);
        foreach (var stale in Directory.GetFiles(outputDir, "*.png"))
            File.Delete(stale);

        var cleanVm = MainViewModelTestHelper.CreateMainViewModel();
        var cleanDesign = ChipletEdgeCouplerJourneyDesign.BuildComposed();
        RunValidation(cleanVm, cleanDesign);
        Capture(cleanVm, Path.Combine(outputDir, "01-design-checks-clean.png"));

        var shiftedVm = MainViewModelTestHelper.CreateMainViewModel();
        var shiftedDesign = ChipletEdgeCouplerJourneyDesign.BuildComposed();
        shiftedDesign.ChipletB.MoveGroup(0, 2.0);
        RunValidation(shiftedVm, shiftedDesign);
        Capture(shiftedVm, Path.Combine(outputDir, "02-design-checks-lateral-offset.png"));

        WriteManifest(outputDir);
        Directory.GetFiles(outputDir, "*.png").Length.ShouldBe(2);
    }

    /// <summary>Runs the validation exactly like the acceptance journey does (#936 wiring).</summary>
    private static void RunValidation(
        CAP.Avalonia.ViewModels.MainViewModel vm, ChipletEdgeCouplerJourneyDesign design)
    {
        var externalPortPins = design.Canvas.Components
            .SelectMany(c => c.Component is ComponentGroup group
                ? group.ExternalPins
                : Enumerable.Empty<GroupPin>())
            .Select(pin => pin.InternalPin!)
            .ToList();
        vm.RightPanel.DesignValidation.RunValidation(
            design.Canvas.ConnectionManager.Connections,
            allComponents: design.Canvas.Components.Select(c => c.Component),
            processLockActive: false,
            externalPortPins: externalPortPins);
    }

    private static void Capture(CAP.Avalonia.ViewModels.MainViewModel vm, string path)
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
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void WriteManifest(string outputDir)
    {
        const string manifest = """
        [
          {"file": "01-design-checks-clean.png", "caption": "Design Checks tab on the two-chiplet edge-coupler journey as built: no chiplet-interface findings."},
          {"file": "02-design-checks-lateral-offset.png", "caption": "Same tab after chiplet B is shifted 2 µm perpendicular to the link: the DRC-lite warning names both chiplets and the offset."}
        ]
        """;
        ScreenshotArtifacts.WriteText(Path.Combine(outputDir, "manifest.json"), manifest);
    }

    /// <summary>Repo-root <c>artifacts/ui-screenshots/issue-1219</c> (or <c>UI_SHOT_DIR/issue-1219</c>).</summary>
    private static string ResolveOutputDirectory()
    {
        var envDir = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (!string.IsNullOrEmpty(envDir))
            return Path.Combine(envDir, "issue-1219");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "artifacts", "ui-screenshots", "issue-1219");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-screenshots", "issue-1219");
    }
}
