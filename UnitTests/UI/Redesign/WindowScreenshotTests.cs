using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Settings;
using CAP.Avalonia.Views;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.UI.Redesign;

/// <summary>
/// Opt-in captures of the migrated tool windows (settings pages, empty process editor,
/// AI assistant) for the design-system PR. No-op unless <c>UI_SHOT_DIR</c> is set; frames
/// land in <c>$UI_SHOT_DIR/ui-redesign/windows/</c>.
/// </summary>
[Collection("LocalizationSingleton")]
[Trait("Category", "UiScreenshots")]
public class WindowScreenshotTests
{
    private static string? OutputDirectory()
    {
        var root = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (string.IsNullOrEmpty(root))
            return null;
        var dir = Path.Combine(root, "ui-redesign", "windows");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Save(Window window, string path)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        frame.ShouldNotBeNull("render miss");
        using (frame)
            ScreenshotArtifacts.SavePng(frame!, path);
    }

    [AvaloniaFact]
    public void CaptureSettingsPages()
    {
        var dir = OutputDirectory();
        if (dir is null)
            return;

        var prefs = new UserPreferencesService(Path.Combine(Path.GetTempPath(), $"win-shot-{Guid.NewGuid():N}.json"));
        var canvas = new DesignCanvasViewModel();
        var vm = new SettingsWindowViewModel(new ISettingsPage[]
        {
            new Page("Settings.Section.GridAlignment", "Icon.Grid3x3",
                new GridSnapSettingsViewModel(new GridSnapSettings(), new AlignmentGuideViewModel())),
            new Page("Settings.Section.Routing", "Icon.Route", new RoutingSettingsViewModel(canvas)),
            new Page("Settings.Interconnect.Title", "Icon.Cable", new InterconnectSettingsViewModel(prefs)),
            new Page("Settings.Language.Title", "Icon.Languages",
                new LanguageSettingsViewModel(LocalizationService.Instance, prefs)),
        });
        var window = new SettingsWindow { DataContext = vm };
        window.Show();
        try
        {
            for (var i = 0; i < vm.Pages.Count; i++)
            {
                vm.SelectedPage = vm.Pages[i];
                Save(window, Path.Combine(dir, $"settings-{i + 1}.png"));
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CaptureEmptyProcessEditor()
    {
        var dir = OutputDirectory();
        if (dir is null)
            return;

        var window = new ProcessManagementWindow
        {
            DataContext = new ProcessManagementViewModel(new CAP_DataAccess.Components.ComponentDraftMapper.BundledPdkCatalog(AppContext.BaseDirectory), Mock.Of<IFileDialogService>()),
        };
        window.Show();
        try
        {
            Save(window, Path.Combine(dir, "process-empty.png"));
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class Page : LocalizedSettingsPage
    {
        public Page(string titleKey, string icon, object viewModel)
            : base(titleKey, LocalizationService.Instance)
        {
            Icon = icon;
            ViewModel = viewModel;
        }

        public override string Icon { get; }
        public override object ViewModel { get; }
    }
}
