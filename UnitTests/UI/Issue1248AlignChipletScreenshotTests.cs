using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Views.Panels;
using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Issue #1248 (rung 6): the Design Checks tab offers an "Align chiplet" one-click fix
/// on chiplet-interface findings — next to Prev/Next, only while the current issue is a
/// <see cref="DesignIssueType.ChipletInterfaceLateralOffset"/>/<see cref="DesignIssueType.ChipletInterfaceGapLoss"/>
/// style finding with a connection. The structural fact (button visible on the finding,
/// hidden otherwise) is asserted unconditionally; PNGs + manifest.json land in
/// <c>artifacts/ui-screenshots/issue-1248/</c> when UI_SHOT_DIR is set.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1248AlignChipletScreenshotTests
{
    private const int CaptureAttempts = 3;
    private const double PanelWidth = 780;
    private const double PanelHeight = 300;
    private const double WavelengthNm = 1550;

    /// <summary>The tab shows the "Align chiplet" button while a chiplet finding is current.</summary>
    [AvaloniaFact]
    public void DesignChecksTab_OffersAlignButton_OnChipletFinding()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var design = ChipletEdgeCouplerJourneyDesign.BuildComposed();
        design.ChipletB.MoveGroup(10.0, 2.0); // gap + lateral offset
        RunValidation(vm, design);

        vm.RightPanel.DesignValidation.IsCurrentIssueAlignable
            .ShouldBeTrue("a chiplet-interface finding with a connection must offer the fix");

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
            var alignButton = window.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.Content as string == "Align chiplet");
            alignButton.ShouldNotBeNull("the Design Checks tab must show the Align chiplet button");
            alignButton.IsVisible.ShouldBeTrue();
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>The button hides once the checks come back clean after the fix.</summary>
    [AvaloniaFact]
    public void DesignChecksTab_HidesAlignButton_WhenClean()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var design = ChipletEdgeCouplerJourneyDesign.BuildComposed();
        design.ChipletB.MoveGroup(10.0, 2.0);
        RunValidation(vm, design);

        // What the button achieves: snap chiplet B back into butt-coupling, re-run.
        var link = design.Canvas.ConnectionManager.Connections.Single(c =>
            ChipletInterfaceChecker.TryGetFacet(c.StartPin, out var s)
            && ChipletInterfaceChecker.TryGetFacet(c.EndPin, out var e)
            && !ReferenceEquals(s.Chiplet, e.Chiplet));
        new ChipletLinkAligner().TryPlan(
                link, design.Canvas.ConnectionManager.Connections,
                design.Canvas.Components.Select(c => c.Component), WavelengthNm,
                out var plan, out _)
            .ShouldBeTrue();
        design.ChipletB.MoveGroup(plan!.DeltaX, plan.DeltaY);
        RunValidation(vm, design);

        vm.RightPanel.DesignValidation.HasIssues.ShouldBeFalse("the aligned design must be clean");
        vm.RightPanel.DesignValidation.IsCurrentIssueAlignable.ShouldBeFalse();
    }

    /// <summary>Captures the Checks tab: the finding with the fix button vs. clean after aligning.</summary>
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

        var warnVm = MainViewModelTestHelper.CreateMainViewModel();
        var warnDesign = ChipletEdgeCouplerJourneyDesign.BuildComposed();
        warnDesign.ChipletB.MoveGroup(10.0, 2.0);
        RunValidation(warnVm, warnDesign);
        Capture(warnVm, Path.Combine(outputDir, "01-design-checks-align-offer.png"));

        var cleanVm = MainViewModelTestHelper.CreateMainViewModel();
        var cleanDesign = ChipletEdgeCouplerJourneyDesign.BuildComposed();
        cleanDesign.ChipletB.MoveGroup(10.0, 2.0);
        var link = cleanDesign.Canvas.ConnectionManager.Connections.Single(c =>
            ChipletInterfaceChecker.TryGetFacet(c.StartPin, out var s)
            && ChipletInterfaceChecker.TryGetFacet(c.EndPin, out var e)
            && !ReferenceEquals(s.Chiplet, e.Chiplet));
        new ChipletLinkAligner().TryPlan(
                link, cleanDesign.Canvas.ConnectionManager.Connections,
                cleanDesign.Canvas.Components.Select(c => c.Component), WavelengthNm,
                out var plan, out _)
            .ShouldBeTrue();
        cleanDesign.ChipletB.MoveGroup(plan!.DeltaX, plan.DeltaY);
        RunValidation(cleanVm, cleanDesign);
        Capture(cleanVm, Path.Combine(outputDir, "02-design-checks-aligned.png"));

        WriteManifest(outputDir);
        Directory.GetFiles(outputDir, "*.png").Length.ShouldBe(2);
    }

    /// <summary>Runs the validation exactly like the #1238 capture does.</summary>
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
            externalPortPins: externalPortPins,
            wavelengthNm: WavelengthNm);
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
          {"file": "01-design-checks-align-offer.png", "caption": "Design Checks tab on the two-chiplet journey with chiplet B shifted (10 µm gap + 2 µm lateral offset): the findings warn, and the 'Align chiplet' one-click fix sits next to Prev/Next."},
          {"file": "02-design-checks-aligned.png", "caption": "Same tab after the fix snaps chiplet B into butt-coupling: the checks come back clean."}
        ]
        """;
        ScreenshotArtifacts.WriteText(Path.Combine(outputDir, "manifest.json"), manifest);
    }

    /// <summary>Repo-root <c>artifacts/ui-screenshots/issue-1248</c> (or <c>UI_SHOT_DIR/issue-1248</c>).</summary>
    private static string ResolveOutputDirectory()
    {
        var envDir = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (!string.IsNullOrEmpty(envDir))
            return Path.Combine(envDir, "issue-1248");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "artifacts", "ui-screenshots", "issue-1248");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-screenshots", "issue-1248");
    }
}
