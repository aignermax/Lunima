using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Solvers;
using CAP.Avalonia.Views.Dialogs;
using CAP_Core.Solvers.ModeSolver;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Issue #1448: dialog footer buttons must size to their localized label instead of
/// clipping it (German "Schließen" was cut to "Schließe" by a fixed Width="70").
/// The footer buttons now use MinWidth + horizontal padding, so the desired width
/// always covers the rendered text in every shipped language. Also captures
/// headless screenshots of the Mode Solver footer (de, es, en) into
/// <c>docs/pr-media/issue-1448/</c>.
/// </summary>
[Collection("LocalizationSingleton")]
public class ModeSolverDialogFooterLocalizationTests
{
    [AvaloniaFact]
    public void FooterButtons_DesiredWidth_FitsLabel_InEveryLocale()
    {
        try
        {
            foreach (var language in SupportedLanguage.All)
            {
                LocalizationService.Instance.SetLanguage(language.Code);
                var dialog = ShowDialog();
                foreach (var button in FooterButtons(dialog))
                {
                    var label = button.Content as string;
                    label.ShouldNotBeNullOrEmpty($"footer button has no text label in {language.Code}");
                    var textWidth = MeasureTextWidth(button, label!);
                    var contentWidth = button.DesiredSize.Width - button.Padding.Left - button.Padding.Right;
                    contentWidth.ShouldBeGreaterThanOrEqualTo(
                        textWidth,
                        $"footer button '{label}' clips in {language.Code}: " +
                        $"content {contentWidth:0.#} < text {textWidth:0.#}");
                }
                dialog.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
        }
    }

    [AvaloniaFact]
    [Trait("Category", "UiScreenshots")]
    public void CaptureModeSolverFooterScreenshots()
    {
        var outputDir = ResolveOutputDirectory();
        Directory.CreateDirectory(outputDir);
        try
        {
            foreach (var language in new[] { SupportedLanguage.German, SupportedLanguage.Spanish, SupportedLanguage.English })
            {
                LocalizationService.Instance.SetLanguage(language.Code);
                var dialog = ShowDialog();
                CaptureFooter(dialog, Path.Combine(outputDir, $"modesolver-footer-{language.Code}.png"));
                dialog.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
        }
    }

    private static ModeSolverDialog ShowDialog()
    {
        var service = new Mock<IModeSolverService>();
        var dialog = new ModeSolverDialog { DataContext = new ModeSolverViewModel(service.Object) };
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        dialog.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return dialog;
    }

    /// <summary>Visible footer buttons carrying a text label (right-aligned bottom row).</summary>
    private static IEnumerable<Button> FooterButtons(ModeSolverDialog dialog)
    {
        var closeButton = dialog.FindControl<Button>("CloseButton")
            ?? throw new InvalidOperationException("CloseButton not found");
        var footer = closeButton.Parent as Panel
            ?? throw new InvalidOperationException("CloseButton is not inside the footer panel");
        return footer.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsVisible && b.Content is string);
    }

    private static double MeasureTextWidth(Button button, string label)
    {
        var probe = new TextBlock
        {
            Text = label,
            FontSize = button.FontSize,
            FontFamily = button.FontFamily,
            FontWeight = button.FontWeight,
        };
        probe.Measure(new Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width;
    }

    private static void CaptureFooter(ModeSolverDialog dialog, string path)
    {
        var bitmap = dialog.CaptureRenderedFrame();
        bitmap.ShouldNotBeNull("render miss for Mode Solver dialog");
        using (bitmap)
            ScreenshotArtifacts.SavePng(bitmap!, path);
    }

    /// <summary>Repo-root <c>docs/pr-media/issue-1448</c> (walks up from the test output for the .sln).</summary>
    private static string ResolveOutputDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "docs", "pr-media", "issue-1448");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "docs", "pr-media", "issue-1448");
    }
}
