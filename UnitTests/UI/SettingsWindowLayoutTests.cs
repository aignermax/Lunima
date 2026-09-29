using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Settings;
using CAP.Avalonia.Views;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Regression test for issue #1160: the German pin-alignment description on the
/// Grid-Snap settings page was clipped at the right window edge instead of wrapping
/// inside the content area. Renders the real SettingsWindow headless and asserts the
/// description TextBlock stays within the content viewport and wraps to multiple lines.
/// </summary>
[Collection("LocalizationSingleton")]
public class SettingsWindowLayoutTests
{
    private const double SidebarWidth = 200;
    private const double DividerWidth = 1;
    private const double ContentHorizontalPadding = 2 * 24;

    [AvaloniaFact]
    public void GridSnapDescription_German_WrapsInsideContentArea()
    {
        LocalizationService.Instance.SetLanguage(SupportedLanguage.German.Code);
        try
        {
            var vm = new SettingsWindowViewModel(new ISettingsPage[]
            {
                new StubPage(new GridSnapSettingsViewModel(new GridSnapSettings(), new AlignmentGuideViewModel()))
            });
            var window = new SettingsWindow { DataContext = vm };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var expectedText = LocalizationService.Instance.Translate("Settings.GridSnap.GuidesDescription");
                var textBlock = window.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .First(t => t.Text == expectedText);

                // Precondition: unwrapped, the sentence really is wider than the pane,
                // so a missing TextWrapping/width constraint would reproduce the clipping.
                var probe = new TextBlock { Text = expectedText, FontSize = textBlock.FontSize };
                probe.Measure(Size.Infinity);
                var contentWidth = window.Bounds.Width - SidebarWidth - DividerWidth - ContentHorizontalPadding;
                probe.DesiredSize.Width.ShouldBeGreaterThan(contentWidth);

                textBlock.Bounds.Width.ShouldBeLessThanOrEqualTo(
                    contentWidth, "description must be constrained to the content area, not clipped at the window edge");
                textBlock.Bounds.Height.ShouldBeGreaterThan(
                    probe.DesiredSize.Height * 1.5, "description should wrap onto at least two lines");
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
        }
    }

    private sealed class StubPage : ISettingsPage
    {
        public StubPage(object viewModel) => ViewModel = viewModel;

        public string Title => "Stub";
        public string Icon => "?";
        public string? Category => null;
        public object ViewModel { get; }
    }
}
