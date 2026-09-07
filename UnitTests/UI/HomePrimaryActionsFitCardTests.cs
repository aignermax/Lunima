using Avalonia;
using Avalonia.Controls;
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
/// The Home card has a fixed width, so its primary-action buttons must fit inside it in
/// every shipped language — long labels ("Lunima kennenlernen", "Beim Rechnen zusehen")
/// must wrap onto a further row instead of running past the card's right edge.
/// </summary>
public class HomePrimaryActionsFitCardTests
{
    private const double WindowWidth = 900;
    private const double WindowHeight = 720;

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("ja")]
    [InlineData("zh-Hans")]
    public void PrimaryActionButtons_StayInsideTheCard(string languageCode)
    {
        var preferencesPath = Path.Combine(Path.GetTempPath(), $"home-fit-prefs-{Guid.NewGuid():N}.json");
        LocalizationService.Instance.SetLanguage(languageCode);
        try
        {
            var preferences = new UserPreferencesService(preferencesPath);
            var home = new HomeViewModel(new RecentProjectsService(preferences), preferences, new ExampleDesignsService());
            var window = new Window { Width = WindowWidth, Height = WindowHeight, Content = new HomeView { DataContext = home } };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var card = window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "HomeCard");
            var contentRight = card.Bounds.Width - card.Padding.Right;
            var buttons = card.GetVisualDescendants().OfType<Button>().Where(b => b.Command != null).ToList();
            buttons.Count.ShouldBeGreaterThanOrEqualTo(4, "the card shows New, Open and the two tour buttons");
            foreach (var button in buttons)
            {
                var origin = button.TranslatePoint(new Point(0, 0), card)!.Value;
                var right = origin.X + button.Bounds.Width;
                (origin.X >= card.Padding.Left && right <= contentRight).ShouldBeTrue(
                    $"[{languageCode}] button '{ButtonLabel(button)}' spans x={origin.X:0}..{right:0} " +
                    $"but the card content ends at {contentRight:0}");
            }
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
            if (File.Exists(preferencesPath))
                File.Delete(preferencesPath);
        }
    }

    private static string ButtonLabel(Button button) =>
        string.Join(" ", button.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
}
