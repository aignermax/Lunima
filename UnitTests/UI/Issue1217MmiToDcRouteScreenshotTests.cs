using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;
using Component = CAP_Core.Components.Core.Component;

namespace UnitTests.UI;

/// <summary>
/// Visual proof for issue #1217 (the targeting half of #1161): the real Connect-mode journey —
/// 2x2 MMI at x≈600, Broadband DC TE 1550 at x≈1100, MMI right upper port click-connected to
/// the DC's left upper port — renders a visible waveguide across the ~250µm gap with a
/// length/loss label of ~251µm, not the reported invisible "5µm, 0.00dB" self-connection on
/// the DC. The structural half is asserted headlessly by
/// <c>UnitTests.Connections.MmiToDcPinTargetingTests</c>; this capture commits the rendered
/// evidence. PNG + manifest.json go to <c>artifacts/ui-screenshots/issue-1217/</c> (opt-in via
/// UI_SHOT_DIR) and are copied to <c>docs/pr-media/issue-1217/</c> for the PR.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1217MmiToDcRouteScreenshotTests
{
    private const int CaptureAttempts = 3;

    [AvaloniaFact]
    public async Task CaptureMmiToDcConnectModeJourney()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UI_SHOT_DIR")))
            return;
        var dir = ResolveOutputDirectory();
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        // The real components of the #1161 journey, at the reported coordinates.
        var canvas = new DesignCanvasViewModel();
        var mmiTemplate = TestPdkLoader.LoadFromPdk("demo-pdk.json")
            .Single(t => t.Name == "2x2 MMI Coupler");
        var dcTemplate = TestPdkLoader.LoadFromPdk("siepic-ebeam-pdk.json")
            .Single(t => t.Name == "Broadband DC TE 1550");
        var mmi = ComponentTemplates.CreateFromTemplate(mmiTemplate, 600, 400);
        var dc = ComponentTemplates.CreateFromTemplate(dcTemplate, 1100, 400);
        canvas.AddComponent(mmi, mmiTemplate.Name, mmiTemplate.PdkSource);
        canvas.AddComponent(dc, dcTemplate.Name, dcTemplate.PdkSource);

        var mainVm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        mainVm.CanvasInteraction.CurrentMode = InteractionMode.Connect;

        // Click-to-connect at the rendered marker positions (hover, then click on each marker).
        var (mmiX, mmiY) = Pin(mmi, "out1").GetAbsolutePosition();
        var (dcX, dcY) = Pin(dc, "port 1").GetAbsolutePosition();
        mainVm.CanvasInteraction.CanvasMouseMove(mmiX, mmiY);
        mainVm.CanvasInteraction.CanvasClicked(mmiX, mmiY);
        mainVm.CanvasInteraction.CanvasMouseMove(dcX, dcY);
        mainVm.CanvasInteraction.CanvasClicked(dcX, dcY);
        await canvas.RecalculateRoutesAsync();

        var connection = canvas.Connections.ShouldHaveSingleItem();
        connection.IsSelected = true; // selection gates the length/loss label, like in the field report

        // Frame both components and the route: world x 540..1240, y 330..530 at zoom 2.
        var designCanvas = new DesignCanvas
        {
            ViewModel = canvas,
            MainViewModel = mainVm,
            Zoom = 2.0,
        };
        canvas.PanX = -540 * designCanvas.Zoom;
        canvas.PanY = -330 * designCanvas.Zoom;
        var window = new Window { Width = 1400, Height = 400, Content = designCanvas };
        window.Show();
        try
        {
            Capture(window, Path.Combine(dir, "01-mmi-to-dc-visible-route.png"));
            ScreenshotArtifacts.WriteText(Path.Combine(dir, "manifest.json"),
                JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        file = "01-mmi-to-dc-visible-route.png",
                        caption = "Issue #1161 journey on dev-ki: Connect mode, 2x2 MMI (x=600) "
                            + "right upper port click-connected to the Broadband DC TE 1550 (x=1100) "
                            + "left upper port. A visible waveguide spans the ~250µm gap and the "
                            + "selected connection's label reads ~251µm — the invisible 5µm "
                            + "self-connection on the DC is not reproducible.",
                    },
                }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static CAP_Core.Components.Core.PhysicalPin Pin(Component component, string name) =>
        component.PhysicalPins.Single(p => p.Name == name);

    private static void Capture(Window window, string path)
    {
        Dispatcher.UIThread.RunJobs();
        window.GetVisualDescendants().OfType<DesignCanvas>().FirstOrDefault()?.InvalidateVisual();
        Dispatcher.UIThread.RunJobs();

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
            ScreenshotArtifacts.SavePng(bitmap, path);
    }

    /// <summary>Repo-root <c>artifacts/ui-screenshots/issue-1217</c>, or <c>UI_SHOT_DIR/issue-1217</c>.</summary>
    private static string ResolveOutputDirectory()
    {
        var envDir = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (!string.IsNullOrEmpty(envDir))
            return Path.Combine(envDir, "issue-1217");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "artifacts", "ui-screenshots", "issue-1217");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-screenshots", "issue-1217");
    }
}
