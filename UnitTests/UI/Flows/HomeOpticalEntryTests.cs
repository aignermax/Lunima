using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using CAP.Avalonia.Services.Localization;

namespace UnitTests.UI.Flows;

[Collection("LocalizationSingleton")]
public class HomeOpticalEntryTests
{
    [AvaloniaTheory]
    [InlineData("en", false, 1280)]
    [InlineData("de", false, 1280)]
    [InlineData("en", true, 1280)]
    [InlineData("de", true, 1280)]
    [InlineData("de", false, 1000)]
    [InlineData("es", false, 1000)]
    public async Task VisibleOpticalEntry_StartsRealGuidanceAndKeepsExistingWork(string language, bool hasWork, int width)
    {
        using var host = new UiFlowTestHost();
        host.Window.Width = width; host.Window.Height = 800;
        LocalizationService.Instance.SetLanguage(language);
        var vm = host.Vm;
        var existing = hasWork ? vm.Canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuide()) : null;
        vm.Home.Show();
        await Task.Delay(180);
        UiInput.RunJobs();
        var home = UiInput.Descendants<CAP.Avalonia.Views.HomeView>(host.Window).Single();
        var button = UiInput.Descendants<Button>(home)
            .Single(b => b.IsEffectivelyVisible && ReferenceEquals(b.Command, vm.LearningPath.ResumeCommand));
        var title = UiInput.Descendants<TextBlock>(button).Single(t => t.Text == vm.LearningPath.ResumeLabel);
        Assert.True(title.DesiredSize.Height <= title.Bounds.Height);
        Assert.Contains(UiInput.Descendants<Button>(host.Window), b => b.IsEffectivelyVisible && ReferenceEquals(b.Command, vm.Home.NewProjectCommand));
        Assert.Contains(UiInput.Descendants<Button>(host.Window), b => b.IsEffectivelyVisible && ReferenceEquals(b.Command, vm.Home.OpenProjectCommand));
        Assert.True(button.Bounds.Height > 0);
        if (!hasWork)
        {
            var folder = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "home-optical");
            Directory.CreateDirectory(folder);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var bitmap = new RenderTargetBitmap(new PixelSize(width, 800));
            bitmap.Render(host.Window);
            bitmap.Save(Path.Combine(folder, $"{language}-{width}.png"));
        }
        UiInput.Click(host.Window, button);
        UiInput.RunJobs();
        Assert.True(vm.Tutorial.IsActive);
        Assert.False(vm.Home.IsHomeVisible);
        if (existing != null) Assert.Contains(existing, vm.Canvas.Components);
    }
}
