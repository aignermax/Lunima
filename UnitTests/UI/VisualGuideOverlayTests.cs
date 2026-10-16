using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls.TourAnchor;
using CAP.Avalonia.Behaviors;

namespace UnitTests.UI;

public class VisualGuideOverlayTests
{
    [AvaloniaFact]
    public void ClippedTarget_DoesNotPointAtUnrelatedVisibleSurface()
    {
        var target = new Button { Name = "ClippedAction", Width = 80, Height = 30, Margin = new Thickness(120, 0, 0, 0) };
        var clipped = new Border { Width = 100, Height = 50, ClipToBounds = true, Child = target };
        var overlay = new TourAnchorOverlay { Demonstrate = true, TargetName = target.Name };
        var window = new Window { Width = 800, Height = 600, Content = new Avalonia.Controls.Grid { Children = { clipped, overlay } } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            overlay.TargetName = null;
            overlay.TargetName = target.Name;
            var pointer = overlay.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "GuidePointer");
            Assert.False(pointer.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Demonstration_PointsAtRealControl_WithoutInterceptingInput()
    {
        var target = new Button { Name = "ActualAction", Content = "Action", Width = 90, Height = 40 };
        var overlay = new TourAnchorOverlay { Demonstrate = true, TargetName = target.Name };
        var window = new Window { Width = 800, Height = 600, Content = new Avalonia.Controls.Grid { Children = { target, overlay } } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            // Trigger target resolution after layout, independently of wall-clock ticks.
            overlay.TargetName = null;
            overlay.TargetName = target.Name;
            var pointer = overlay.GetVisualDescendants().OfType<Control>().SingleOrDefault(c => c.Name == "GuidePointer");
            Assert.NotNull(pointer);
            Assert.True(pointer.IsVisible);
            Assert.False(pointer.IsHitTestVisible);
            var timer = (DispatcherTimer?)typeof(TourAnchorOverlay)
                .GetField("_pointerTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(overlay);
            overlay.IsVisible = false;
            Assert.True(timer == null || !timer.IsEnabled);
            Assert.False(pointer.IsVisible);
            overlay.IsVisible = true;
            overlay.TargetName = "DoesNotExist";
            Assert.False(pointer.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ReducedMotion_DemonstrationHasNoAnimationTimer()
    {
        var previous = RevealOnShowBehavior.AnimationsEnabled;
        RevealOnShowBehavior.AnimationsEnabled = false;
        var target = new Button { Name = "Action", Width = 90, Height = 40 };
        var overlay = new TourAnchorOverlay { Demonstrate = true, TargetName = "Action" };
        var window = new Window { Width = 800, Height = 600, Content = new Avalonia.Controls.Grid { Children = { target, overlay } } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            overlay.TargetName = null;
            overlay.TargetName = target.Name;
            var pointer = overlay.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "GuidePointer");
            Assert.True(pointer.IsVisible);
            var timer = (DispatcherTimer?)typeof(TourAnchorOverlay)
                .GetField("_pointerTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(overlay);
            Assert.True(timer == null || !timer.IsEnabled);
        }
        finally
        {
            window.Close();
            RevealOnShowBehavior.AnimationsEnabled = previous;
        }
    }

    [AvaloniaFact]
    public void HiddenGuidance_StopsPolling_AndResumesWhenShown()
    {
        var overlay = new TourAnchorOverlay { IsVisible = false };
        var window = new Window { Content = overlay };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.False(Poll(overlay).IsEnabled);
            overlay.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(Poll(overlay).IsEnabled);
            overlay.IsVisible = false;
            Assert.False(Poll(overlay).IsEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void HiddenParent_AndDetach_StopGuidancePolling()
    {
        var overlay = new TourAnchorOverlay();
        var parent = new Border { Child = overlay };
        var window = new Window { Content = parent };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(Poll(overlay).IsEnabled);
            parent.IsVisible = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(Poll(overlay).IsEnabled);
            parent.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(Poll(overlay).IsEnabled);
            parent.Child = null;
            Assert.False(Poll(overlay).IsEnabled);
        }
        finally { window.Close(); }
    }

    private static DispatcherTimer Poll(TourAnchorOverlay overlay) =>
        (DispatcherTimer)typeof(TourAnchorOverlay)
            .GetField("_poll", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(overlay)!;
}
