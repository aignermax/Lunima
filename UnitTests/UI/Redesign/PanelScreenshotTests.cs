using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Views.Panels;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.UI.Redesign;

/// <summary>
/// Renders the analysis panels (empty state), the help button with its overlay card and a
/// tour card headless, so the redesign PR can show them. No-op without UI_SHOT_DIR; PNGs
/// land in <c>$UI_SHOT_DIR/ui-redesign/panels/</c>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class PanelScreenshotTests
{
    private const int PanelWidth = 640;
    private const int SidebarWidth = 300;

    /// <summary>Captures every analysis/sidebar panel in its initial (empty) state.</summary>
    [AvaloniaFact]
    public void CapturePanels()
    {
        var dir = OutputDirectory();
        if (dir == null)
            return;

        var previous = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
        try
        {
            var vm = MainViewModelTestHelper.CreateMainViewModel();
            Save(dir, "time-domain", new TimeDomainPanel { DataContext = vm }, PanelWidth);
            Save(dir, "eye-diagram", new EyeDiagramPanel { DataContext = vm }, PanelWidth);
            Save(dir, "spectrum", new WavelengthSpectrumPanel { DataContext = vm }, PanelWidth);
            Save(dir, "monte-carlo", new MonteCarloPanel { DataContext = vm }, PanelWidth);
            Save(dir, "logic", new LogicPanel { DataContext = vm }, PanelWidth);
            Save(dir, "design-checks", new DesignChecksPanel { DataContext = vm }, PanelWidth);
            Save(dir, "netlist", new NetlistPanel { DataContext = vm }, PanelWidth);
            Save(dir, "parameter-sweep", new ParameterSweepPanel { DataContext = vm }, PanelWidth);
            Save(dir, "truth-table", new TruthTablePanel { DataContext = vm }, SidebarWidth);
            Save(dir, "routing-diagnostics", new RoutingDiagnosticsPanel { DataContext = vm }, SidebarWidth);
            Save(dir, "layout-compression", new LayoutCompressionPanel { DataContext = vm }, SidebarWidth);
            Save(dir, "component-dimension", new ComponentDimensionDiagnosticsPanel { DataContext = vm }, SidebarWidth);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    /// <summary>Captures the help button next to a heading and the overlay card of its flyout content.</summary>
    [AvaloniaFact]
    public void CaptureHelpButtonAndCard()
    {
        var dir = OutputDirectory();
        if (dir == null)
            return;

        var previous = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
        try
        {
            var title = new TextBlock { Text = "Logic analysis", VerticalAlignment = VerticalAlignment.Center };
            title.Classes.Add("subtitle");
            var header = new Avalonia.Controls.Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12) };
            header.Children.Add(title);
            var help = new HelpFlyoutButton { Title = "Logic help" };
            Avalonia.Controls.Grid.SetColumn(help, 1);
            header.Children.Add(help);
            Save(dir, "help-button", header, 360);

            var card = new Border { Margin = new Thickness(24), Width = 440, Child = new StackPanel { Spacing = 8, Children =
            {
                Heading("Logic help"),
                new LogicHelpFlyout(),
            } } };
            card.Classes.Add("overlay");
            Save(dir, "help-card", card, 520);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    private static TextBlock Heading(string text)
    {
        var block = new TextBlock { Text = text };
        block.Classes.Add("subtitle");
        return block;
    }

    private static string? OutputDirectory()
    {
        var root = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (string.IsNullOrEmpty(root))
            return null;
        var dir = Path.Combine(root, "ui-redesign", "panels");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Save(string dir, string name, Control content, int width)
    {
        var host = new Border { Background = (Avalonia.Media.IBrush?)Application.Current!.FindResource("Brush.Surface.Panel"), Child = content };
        var window = new Window { Width = width, SizeToContent = SizeToContent.Height, Content = host };
        window.Show();
        Pump();
        using var bitmap = window.CaptureRenderedFrame();
        bitmap.ShouldNotBeNull(name);
        ScreenshotArtifacts.SavePng(bitmap!, Path.Combine(dir, name + ".png"));
        window.Close();
        Pump();
    }

    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }
}
