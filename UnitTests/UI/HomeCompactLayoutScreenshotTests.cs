using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Home;
using CAP.Avalonia.Views;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// The Home screen is laid out wide rather than tall (recent projects left, learning
/// steps and example tiles right): it must fit a laptop-height window. Renders PR media
/// when <c>UI_SHOT_DIR</c> is set.
/// </summary>
[Collection("LocalizationSingleton")]
public sealed class HomeCompactLayoutScreenshotTests : IDisposable
{
    private const double LaptopWindowHeight = 760;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "home-compact-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        LocalizationService.Instance.SetLanguage("en");
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public void HomeCard_FitsALaptopHeightWindow(string language)
    {
        LocalizationService.Instance.SetLanguage(language);
        Directory.CreateDirectory(_root);
        var preferences = new UserPreferencesService(Path.Combine(_root, "prefs.json"));
        var recents = new RecentProjectsService(preferences);
        foreach (var name in new[] { "ring-filter-sweep", "mzi-tuning", "chiplet-link", "lab-measurement-overlay", "tapeout-v3" })
        {
            var path = Path.Combine(_root, name + ".lun");
            File.WriteAllText(path, "{}");
            recents.RecordProject(path);
        }
        var home = new HomeViewModel(recents, preferences, new ExampleDesignsService());
        var window = new Window { Width = 1280, Height = LaptopWindowHeight, Content = new HomeView { DataContext = home } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var card = window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "HomeCard");
        card.Bounds.Height.ShouldBeLessThan(LaptopWindowHeight - 40, "the whole card is visible without scrolling the window");

        var shotRoot = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (!string.IsNullOrEmpty(shotRoot))
        {
            var dir = Path.Combine(shotRoot, "home-compact");
            Directory.CreateDirectory(dir);
            using var frame = window.CaptureRenderedFrame();
            ScreenshotArtifacts.SavePng(frame!, Path.Combine(dir, $"home-{language}.png"));
        }
        window.Close();
    }
}
