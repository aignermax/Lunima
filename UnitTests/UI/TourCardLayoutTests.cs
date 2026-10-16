using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Controls.TourAnchor;

namespace UnitTests.UI;

public class TourCardLayoutTests
{
    [AvaloniaFact]
    public void CardResize_RepositionsWithinSameLayout_WithoutWaitingForPoll()
    {
        var card = new Border { Width = 280, Height = 40 };
        var overlay = new TourAnchorOverlay { Card = card, Demonstrate = true };
        var window = new Window { Width = 800, Height = 600, Content = overlay };
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var poll = (DispatcherTimer)typeof(TourAnchorOverlay).GetField("_poll",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(overlay)!;
            poll.Stop(); // This regression must not be rescued by the 150 ms poll.
            var host = overlay.FindControl<ContentControl>("CardHost")!;
            card.Height = 180;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var root = overlay.FindControl<global::Avalonia.Controls.Canvas>("RootCanvas")!;
            Assert.Equal(root.Bounds.Height - host.Bounds.Height - 28, global::Avalonia.Controls.Canvas.GetTop(host), precision: 4);
            Assert.True(double.IsFinite(global::Avalonia.Controls.Canvas.GetTop(host)));
            Assert.True(host.Bounds.Bottom <= root.Bounds.Height);
            card.Height = 40;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(root.Bounds.Height - host.Bounds.Height - 28, global::Avalonia.Controls.Canvas.GetTop(host), precision: 4);
        }
        finally { window.Close(); }
    }
}
