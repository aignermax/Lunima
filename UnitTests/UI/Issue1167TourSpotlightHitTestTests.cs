using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls.TourAnchor;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Regression tests for the #1167 spotlight overlay's non-modal contract: the dim
/// layer must be hit-test transparent so canvas drag/drop still works mid-tour,
/// while the tour card's Skip/Next buttons stay clickable. A root-level
/// IsHitTestVisible="False" would pass clicks through but also kill the card
/// (Avalonia prunes the whole subtree), so non-modality must come from the
/// decorations alone being hit-test transparent.
/// </summary>
public class Issue1167TourSpotlightHitTestTests
{
    /// <summary>The filter pointer input effectively uses (GetVisualAt's default only checks IsVisible).</summary>
    private static bool InputHitTestFilter(Visual v)
        => v is IInputElement e && e.IsHitTestVisible && e.IsEffectivelyVisible;

    [AvaloniaFact]
    public void DimAndSpotlight_PassClicksThrough_CardStaysClickable()
    {
        var underlying = new Button
        {
            Content = "canvas surface",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        var target = new Border
        {
            Name = "SpotlightTarget",
            Width = 80,
            Height = 32,
            Background = Brushes.DimGray,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(600, 100, 0, 0),
        };
        var card = new Button { Content = "Next", Width = 200, Height = 80 };
        var overlay = new TourAnchorOverlay { Card = card };
        var window = new Window
        {
            Width = 1200,
            Height = 800,
            Content = new Avalonia.Controls.Grid { Children = { underlying, target, overlay } },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        overlay.TargetName = target.Name;
        // Hit testing reads the last committed composition state, which can lag the
        // layout pass that moved the card — commit a few frames before hit-testing.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            using var frame = window.CaptureRenderedFrame();
        }

        var dimHit = window.GetVisualAt(new Point(100, 600), InputHitTestFilter);
        Assert.True(IsInside(dimHit, underlying),
            $"clicks in the dimmed area must reach the control underneath, got {Describe(dimHit)}");

        var holeHit = window.GetVisualAt(new Point(640, 116), InputHitTestFilter);
        Assert.True(IsInside(holeHit, target),
            $"the spotlight hole must leave the spotlighted target clickable, got {Describe(holeHit)}");

        var cardHost = overlay.GetVisualDescendants().OfType<ContentControl>().First(c => c.Name == "CardHost");
        var cardOrigin = cardHost.TranslatePoint(default, window)!.Value;
        var cardCenter = cardOrigin + new Point(cardHost.Bounds.Width / 2, cardHost.Bounds.Height / 2);
        var cardHit = window.GetVisualAt(cardCenter, InputHitTestFilter);
        Assert.True(IsInside(cardHit, card),
            $"the tour card's Skip/Next buttons must stay clickable, got {Describe(cardHit)}");

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private static bool IsInside(object? hit, Visual container)
        => hit is Visual v && (v == container || v.GetVisualAncestors().Contains(container));

    private static string Describe(object? hit)
    {
        if (hit is not Visual v)
            return "<null>";
        return string.Join(" <- ", new[] { v }.Concat(v.GetVisualAncestors().Take(6))
            .Select(x => $"{x.GetType().Name}[{(x as Control)?.Name ?? "-"}]"));
    }
}
