using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Views.Panels;

namespace UnitTests.UI.Flows;

[Collection("LocalizationSingleton")]
public class LearningPathFlowTests
{
    [AvaloniaTheory]
    [InlineData("de", 1440)]
    [InlineData("en", 1100)]
    public async Task RealHomeStartsPersistentPathAndCollapseKeepsCanvasUsable(string language, int width)
    {
        using var host = new UiFlowTestHost();
        host.Window.Width = width; host.Window.Height = 900;
        LocalizationService.Instance.SetLanguage(language);
        host.Vm.Home.Show();
        await Task.Delay(150); UiInput.RunJobs();
        var start = UiInput.Descendants<Button>(host.Window).Single(b => b.IsEffectivelyVisible && ReferenceEquals(b.Command, host.Vm.LearningPath.ResumeCommand));
        UiInput.Click(host.Window, start); UiInput.RunJobs();
        Assert.True(host.Vm.Tutorial.IsActive);
        Assert.True(host.Vm.LearningPath.IsVisible);
        Assert.Equal(7, host.Vm.LearningPath.Chapters.Count);
        var panel = UiInput.Descendants<LearningPathPanel>(host.Window).Single();
        Assert.True(panel.IsEffectivelyVisible);
        Assert.Equal(244, panel.Bounds.Width);
        Capture(host.Window, width, language + "-expanded");
        var collapse = UiInput.Descendants<Button>(panel).First(b => b.IsEffectivelyVisible && ReferenceEquals(b.Command, host.Vm.LearningPath.ToggleCommand));
        UiInput.Click(host.Window, collapse); UiInput.RunJobs();
        Assert.False(host.Vm.LearningPath.IsExpanded);
        Assert.Equal(44, panel.Bounds.Width);
        Assert.True(host.Vm.Tutorial.IsActive);
        Capture(host.Window, width, language + "-collapsed");
        host.Vm.LearningPath.CloseCommand.Execute(null); UiInput.RunJobs();
        Assert.False(host.Vm.Tutorial.IsActive);
        Assert.False(host.Vm.LearningPath.IsVisible);
        Assert.Equal(0, host.Vm.LearningPath.CompletedCount);
    }

    private static void Capture(Window window, int width, string name)
    {
        var folder = Path.Combine(Path.GetTempPath(), "lunima-learning-path-preview");
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, 900));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(folder, name + ".png"));
    }
}
