using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls.GdsImport;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.GdsImport.Interpretation;
using CAP_DataAccess.Import.Gds;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for #1570: an unmapped reviewed cell with exactly two
/// optical pins is simulated as a lossless pass-through — the review must say so.
/// Captures the Model-stage hint and the per-cell Summary rows of the
/// interpretation review. PNGs + manifest.json land in
/// <c>docs/pr-media/issue-1570/</c> (only with <c>CAP_UPDATE_PR_MEDIA=1</c>;
/// otherwise a temp dir — see <see cref="ScreenshotArtifacts.ResolvePrMediaDirectory"/>).
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1570PassThroughHintScreenshotTests
{
    private sealed record ManifestEntry(string File, string Caption);

    [AvaloniaFact]
    public async Task PassThroughAssumptionIsVisibleInModelStageAndSummary()
    {
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1570");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);
        var manifest = new List<ManifestEntry>();

        var vm = await CreateReview();
        var view = new GdsInterpretationReviewView { DataContext = vm };
        var window = new Window { Width = 1180, Height = 780, Content = view };
        try
        {
            window.Show();
            await vm.NextCommand.ExecuteAsync(null);
            await vm.NextCommand.ExecuteAsync(null);
            vm.Cells[0].IsPassThroughAssumed.ShouldBeTrue("fixture: two optical pins, no mapped model");
            vm.PauseGuideCommand.Execute(null);

            vm.StageIndex = 4;
            Layout(window);
            var hint = view.FindControl<TextBlock>("GdsReviewPassThroughHint").ShouldNotBeNull();
            hint.IsEffectivelyVisible.ShouldBeTrue("the Model stage names the assumed lossless pass-through");
            AssertInsideView(view, hint);
            Capture(view, Path.Combine(dir, "01-model-stage-pass-through-hint.png"),
                "Review, stage 5 (Models): with no model mapped, the two-optical-pin cell announces " +
                "'No model mapped — simulated as a lossless pass-through.' under the model warning.",
                manifest);

            vm.StageIndex = 5;
            Layout(window);
            var summary = view.FindControl<ItemsControl>("GdsReviewPassThroughSummary").ShouldNotBeNull();
            summary.IsEffectivelyVisible.ShouldBeTrue("the Summary lists every cell importing with the assumed model");
            var row = view.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => t.Text?.Contains("lossless pass-through") == true && t != hint)
                .ShouldNotBeNull("the eligible cell gets its own Summary row");
            row.Text.ShouldContain("leaf");
            row.IsEffectivelyVisible.ShouldBeTrue();
            AssertInsideView(view, row);
            Capture(view, Path.Combine(dir, "02-summary-pass-through-row.png"),
                "Review, stage 6 (Summary): the eligible cell's row repeats the assumed lossless " +
                "pass-through model, so the assumption is visible again right before Apply.",
                manifest);
        }
        finally
        {
            vm.Close();
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

    /// <summary>Asserts the control lands fully inside the review view — no dialog clipping.</summary>
    private static void AssertInsideView(Control view, Control control)
    {
        var position = control.TranslatePoint(default, view).ShouldNotBeNull();
        position.X.ShouldBeGreaterThanOrEqualTo(0);
        position.Y.ShouldBeGreaterThanOrEqualTo(0);
        (position.X + control.Bounds.Width).ShouldBeLessThanOrEqualTo(view.Bounds.Width + 1);
        (position.Y + control.Bounds.Height).ShouldBeLessThanOrEqualTo(view.Bounds.Height + 1);
    }

    private static void Capture(Control view, string path, string caption, List<ManifestEntry> manifest)
    {
        Dispatcher.UIThread.RunJobs();
        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(view.Bounds.Width), (int)Math.Ceiling(view.Bounds.Height)));
        bitmap.Render(view);
        ScreenshotArtifacts.SavePng(bitmap, path).Length.ShouldBeGreaterThan(1000);
        manifest.Add(new ManifestEntry(Path.GetFileName(path), caption));
    }

    private static void Layout(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static async Task<GdsInterpretationReviewViewModel> CreateReview()
    {
        var library = new GdsLibrary();
        var leaf = new GdsCell { Name = "leaf" };
        leaf.Elements.Add(new GdsPolygon { Layer = 1, Points = [new(0, 0), new(20, 0), new(20, 4), new(0, 4)] });
        leaf.Elements.Add(new GdsText { Layer = 56, Text = "o1", Position = new(0, 2) });
        leaf.Elements.Add(new GdsText { Layer = 56, Text = "o2", Position = new(20, 2) });
        library.Cells.Add(leaf.Name, leaf);
        var top = new GdsCell { Name = "TOP" };
        top.Elements.Add(new GdsReference { CellName = "leaf" });
        library.Cells.Add(top.Name, top);
        return await GdsInterpretationReviewViewModel.CreateAsync(library, "TOP", "pass-through-hint-shot",
            [new(1, 0, GdsLayerRole.OpticalCore), new(56, 0, GdsLayerRole.PortLabel)], [],
            new GdsLayerProfileStore(Path.Combine(Path.GetTempPath(), "gds-review-pt-shot-" + Guid.NewGuid() + ".json")));
    }
}
