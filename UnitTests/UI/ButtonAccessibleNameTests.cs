using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Home;
using CAP.Avalonia.Views;
using Shouldly;
using UnitTests.UI.Flows;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// A button whose content is an icon or a layout panel has no text for a screen reader or
/// UI automation to read, so Avalonia falls back to the content's type name
/// ("CAP.Avalonia.Controls.Icon", "Avalonia.Controls.StackPanel"). Every app button must
/// carry a real accessible name instead.
/// </summary>
[Collection("LocalizationSingleton")]
public class ButtonAccessibleNameTests
{
    [AvaloniaFact]
    public void HomeView_EveryButton_HasAReadableName()
    {
        var preferencesPath = Path.Combine(Path.GetTempPath(), $"a11y-home-prefs-{Guid.NewGuid():N}.json");
        try
        {
            var preferences = new UserPreferencesService(preferencesPath);
            var recent = new RecentProjectsService(preferences);
            recent.RecordProject(Path.Combine(Path.GetTempPath(), "a11y-sample.lun"));
            var home = new HomeViewModel(recent, preferences, new ExampleDesignsService());
            var window = new Window { Width = 900, Height = 720, Content = new HomeView { DataContext = home } };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            UnnamedButtons(window).ShouldBeEmpty();
            window.Close();
        }
        finally
        {
            if (File.Exists(preferencesPath))
                File.Delete(preferencesPath);
        }
    }

    [AvaloniaFact]
    [Trait("Category", "Slow")]
    public void MainWindow_EveryAppButton_HasAReadableName()
    {
        using var host = new UiFlowTestHost();
        Dispatcher.UIThread.RunJobs();

        UnnamedButtons(host.Window).ShouldBeEmpty();
    }

    private static List<string> UnnamedButtons(Window window) =>
        window.GetVisualDescendants()
            .OfType<Button>()
            .Where(b => b.IsEffectivelyVisible && b.TemplatedParent is not ScrollBar)
            .Select(b => (Button: b, Name: ControlAutomationPeer.CreatePeerForElement(b).GetName()))
            .Where(x => IsTypeNameOrEmpty(x.Name))
            .Select(x => $"{x.Button.GetType().Name} '{x.Button.Name}' [{string.Join(",", x.Button.Classes)}] → '{x.Name}'")
            .Distinct()
            .ToList();

    private static bool IsTypeNameOrEmpty(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.StartsWith("Avalonia.") || name.StartsWith("CAP.");
}
