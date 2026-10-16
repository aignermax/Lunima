using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Controls.TourAnchor;

namespace UnitTests.UI;

public class VisualGuideOverlayTests
{
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
