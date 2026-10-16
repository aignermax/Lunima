using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls.TourAnchor;
using CAP.Avalonia.Behaviors;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels.Canvas;
using Avalonia.Interactivity;

namespace UnitTests.UI;

public class VisualGuideOverlayTests
{
    [AvaloniaFact]
    public void RevealTarget_RemainsClickableDuringAnchorRefresh()
    {
        var vm = new DesignCanvasViewModel();
        var canvas = new DesignCanvas { Name = "Canvas", ViewModel = vm };
        var overlay = new TourAnchorOverlay { Demonstrate = true, TargetName = "Canvas", TargetCanvasPoint = new Point(9000, 8000) };
        var window = new Window { Width = 800, Height = 600, Content = new global::Avalonia.Controls.Grid { Children = { canvas, overlay } } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var refresh = typeof(TourAnchorOverlay).GetMethod("RefreshAnchor", BindingFlags.NonPublic | BindingFlags.Instance)!;
            refresh.Invoke(overlay, null);
            Dispatcher.UIThread.RunJobs();
            var reveal = overlay.FindControl<Button>("GuideShowTarget")!;
            Assert.True(reveal.IsVisible);
            var visibilityDrops = 0;
            reveal.PropertyChanged += (_, e) => { if (e.Property == Visual.IsVisibleProperty && !reveal.IsVisible) visibilityDrops++; };
            refresh.Invoke(overlay, null);
            Assert.Equal(0, visibilityDrops);
            UnitTests.UI.Flows.UiInput.Click(window, reveal);
            Assert.False(reveal.IsVisible);
            Assert.Empty(vm.Components);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CanvasDrag_DemonstratesActualWorldEndpoints_WithoutEditing()
    {
        var vm = new DesignCanvasViewModel();
        var canvas = new DesignCanvas { Name = "Canvas", ViewModel = vm };
        var overlay = new TourAnchorOverlay { Demonstrate = true, TargetName = "Canvas", TargetCanvasPoint = new Point(100, 100) };
        var destinationProperty = typeof(TourAnchorOverlay).GetProperty("TargetCanvasDestinationPoint");
        Assert.NotNull(destinationProperty);
        destinationProperty.SetValue(overlay, new Point(9000, 8000));
        var window = new Window { Width = 800, Height = 600, Content = new Avalonia.Controls.Grid { Children = { canvas, overlay } } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            overlay.TargetName = null;
            overlay.TargetName = "Canvas";
            var reveal = overlay.FindControl<Button>("GuideShowTarget")!;
            Assert.True(reveal.IsVisible);
            reveal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var trail = overlay.FindControl<global::Avalonia.Controls.Shapes.Line>("GuideTrail")!;
            Assert.True(trail.IsVisible);
            Assert.NotEqual(trail.StartPoint, trail.EndPoint);
            Assert.False(reveal.IsVisible);
            Assert.Empty(vm.Components);
            Assert.Empty(vm.Connections);
            overlay.IsVisible = false;
            Assert.False(trail.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void OffscreenConnectionTarget_RevealsBothPins()
    {
        var vm = new DesignCanvasViewModel();
        var startPart = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var endPart = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        startPart.PhysicalX = 100;
        startPart.PhysicalY = 100;
        endPart.PhysicalX = 9000;
        endPart.PhysicalY = 8000;
        var start = startPart.PhysicalPins.First();
        var end = endPart.PhysicalPins.First();
        var canvas = new DesignCanvas { Name = "Canvas", ViewModel = vm };
        var overlay = new TourAnchorOverlay { Demonstrate = true, TargetName = "Canvas", TargetPin = start, ConnectionTargetPin = end };
        var window = new Window { Width = 800, Height = 600, Content = new Avalonia.Controls.Grid { Children = { canvas, overlay } } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            overlay.TargetName = null;
            overlay.TargetName = "Canvas";
            var reveal = overlay.FindControl<Button>("GuideShowTarget")!;
            Assert.True(reveal.IsVisible);
            reveal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            foreach (var pin in new[] { start, end })
            {
                var position = pin.GetAbsolutePosition();
                Assert.True(new Rect(canvas.Bounds.Size).Contains(new Point(
                    position.x * canvas.Zoom + vm.PanX, position.y * canvas.Zoom + vm.PanY)));
            }
            Assert.False(reveal.IsVisible);
            Assert.Equal(100, startPart.PhysicalX);
            Assert.Equal(9000, endPart.PhysicalX);
            Assert.Empty(vm.Connections);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void OffscreenPlacement_CanBeRevealedWithoutChangingDesign()
    {
        var vm = new DesignCanvasViewModel();
        var target = new DesignCanvas { Name = "Canvas", ViewModel = vm };
        var overlay = new TourAnchorOverlay { Demonstrate = true, TargetName = "Canvas", TargetCanvasPoint = new Point(9000, 8000) };
        var window = new Window { Width = 800, Height = 600, Content = new Avalonia.Controls.Grid { Children = { target, overlay } } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            overlay.TargetCanvasPoint = new Point(9001, 8000);
            Dispatcher.UIThread.RunJobs();
            var reveal = overlay.FindControl<Button>("GuideShowTarget");
            Assert.NotNull(reveal);
            Assert.True(reveal.IsVisible);
            reveal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var local = new Point(9001 * target.Zoom + vm.PanX, 8000 * target.Zoom + vm.PanY);
            Assert.True(new Rect(target.Bounds.Size).Contains(local));
            Assert.Empty(vm.Components);
            Assert.Empty(vm.Connections);
            Assert.False(reveal.IsVisible);
        }
        finally { window.Close(); }
    }

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
