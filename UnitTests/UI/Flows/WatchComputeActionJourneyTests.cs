using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls.TourAnchor;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Workspaces;

namespace UnitTests.UI.Flows;

[Collection("LocalizationSingleton")]
public class WatchComputeActionJourneyTests
{
    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public async Task Counter_RealButtonsBuildStepRunStop_WithoutInputInjection(string language)
    {
        using var host = new UiFlowTestHost();
        var window = host.Window;
        window.Width = 1280;
        window.Height = 800;
        LocalizationService.Instance.SetLanguage(language);
        await host.Vm.Home.WatchComputeTourCommand.ExecuteAsync(null);
        Pump();
        var tour = host.Vm.WatchTour;
        var logic = host.Vm.RightPanel.Logic;
        Assert.True(tour.IsActive);
        var overlay = UiInput.Descendants<TourAnchorOverlay>(window)
            .Single(o => o.Card is CAP.Avalonia.Views.Panels.WatchComputeTourPanel);
        Assert.True(overlay.Demonstrate);
        async Task ClickCard(Button button)
        {
            // The overlay polls its placement as the card changes from a cue
            // to recovery/Done. Wait for the actual hit target to settle.
            for (var attempt = 0; attempt < 30; attempt++)
            {
                window.UpdateLayout();
                Pump();
                var before = UiInput.PointIn(window, button);
                window.MouseMove(before);
                await Task.Delay(20);
                Pump();
                var after = UiInput.PointIn(window, button);
                if (before != after) continue;
                var hit = window.InputHitTest(after);
                if (!ReferenceEquals(hit, button) && !(hit is Visual visual && visual.GetVisualAncestors().Contains(button))) continue;
                var clicks = 0;
                void OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => clicks++;
                button.Click += OnClick;
                try
                {
                    window.MouseMove(after);
                    window.MouseDown(after, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                    Assert.True(button.IsPressed, $"{button.Name} press at {after}; now {UiInput.PointIn(window, button)}; enabled {button.IsEnabled}");
                    window.MouseUp(after, MouseButton.Left);
                    Pump();
                }
                finally { button.Click -= OnClick; }
                Assert.Equal(1, clicks);
                return;
            }
            Assert.Fail($"{button.Name} did not become a stable clickable target");
        }
        async Task ClickTarget(int expectedStage)
        {
            Assert.Equal(expectedStage, tour.CurrentStepIndex);
            window.UpdateLayout();
            Pump();
            var button = UiInput.Descendants<Button>(window).Single(b => b.Name == tour.CurrentTargetName);
            Assert.True(button.IsEffectivelyVisible);
            Assert.True(button.IsEnabled);
            var pointer = overlay.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "GuidePointer");
            for (var i = 0; i < 30 && !pointer.IsVisible; i++)
            {
                await Task.Delay(20);
                Pump();
            }
            Assert.True(pointer.IsVisible, $"Stage {expectedStage} must demonstrate the visible control");
            UiInput.Click(window, button);
            for (var i = 0; i < 100 && tour.CurrentStepIndex == expectedStage; i++)
            {
                await Task.Delay(20);
                Pump();
            }
            Assert.Equal(expectedStage + 1, tour.CurrentStepIndex);
        }
        host.Vm.WorkspaceSwitch.Current = Workspace.Layout;
        host.Vm.BottomPanel.Analysis.IsVisible = false;
        Pump();
        Assert.Null(tour.CurrentTargetName);
        var recover = UiInput.Descendants<Button>(window).Single(b => b.Name == "WatchReturnToControlsButton");
        Assert.True(recover.IsEffectivelyVisible);
        window.UpdateLayout();
        Pump();
        await ClickCard(recover);
        Pump();
        Assert.True(host.Vm.WorkspaceSwitch.IsLogic);
        await ClickTarget(0);
        var inputs = logic.Inputs.Select(i => (i.PinName, i.IsOn)).ToArray();
        await ClickTarget(1);
        Assert.True(logic.ClockStepCount > 0);
        await ClickTarget(2);
        Assert.True(logic.IsRunning);
        host.Vm.BottomPanel.Analysis.IsVisible = false;
        Pump();
        Assert.False(tour.IsClosing);
        window.UpdateLayout();
        Pump();
        await ClickCard(recover);
        Pump();
        if (!logic.IsRunning) await ClickTarget(2);
        Assert.False(tour.NeedsControlsRecovery, $"Recovery button at {UiInput.PointIn(window, recover)} must open dock");
        Assert.Equal(3, tour.CurrentStepIndex);
        var folder = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "watch-compute");
        Directory.CreateDirectory(folder);
        using (var bitmap = new RenderTargetBitmap(new PixelSize(1280, 800)))
        {
            bitmap.Render(window);
            bitmap.Save(Path.Combine(folder, language + "-stop.png"));
        }
        await ClickTarget(3);
        Assert.False(logic.IsRunning);
        Assert.Equal(inputs, logic.Inputs.Select(i => (i.PinName, i.IsOn)).ToArray());
        var finish = UiInput.Descendants<Button>(window).Single(b => b.Name == "WatchFinishButton");
        window.UpdateLayout();
        Pump();
        await ClickCard(finish);
        Pump();
        Assert.True(tour.IsCompleted);
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
