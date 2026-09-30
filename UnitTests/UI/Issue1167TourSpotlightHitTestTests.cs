using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
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

    /// <summary>
    /// Verifies the overlay's non-modality: clicks in the dimmed area and inside the
    /// spotlight hole reach the controls underneath, while the tour card stays clickable.
    /// </summary>
    [AvaloniaFact]
    public void DimAndSpotlight_PassClicksThrough_CardStaysClickable()
    {
        // An explicit background keeps the surface hit-testable without relying on the
        // theme resolving a Button/Window background (it does not on the Linux CI runner).
        var underlying = new Border
        {
            Name = "CanvasSurface",
            Background = Brushes.Black,
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
        Dispatcher.UIThread.RunJobs();

        var dimHit = LayoutHitTest(window, new Point(100, 600));
        Assert.True(IsInside(dimHit, underlying),
            $"clicks in the dimmed area must reach the control underneath, got {Describe(dimHit)}");

        var holeHit = LayoutHitTest(window, new Point(640, 116));
        Assert.True(IsInside(holeHit, target),
            $"the spotlight hole must leave the spotlighted target clickable, got {Describe(holeHit)}");

        var cardHost = overlay.GetVisualDescendants().OfType<ContentControl>().First(c => c.Name == "CardHost");
        var cardOrigin = cardHost.TranslatePoint(default, window)!.Value;
        var cardCenter = cardOrigin + new Point(cardHost.Bounds.Width / 2, cardHost.Bounds.Height / 2);
        var cardHit = LayoutHitTest(window, cardCenter);
        Assert.True(IsInside(cardHit, card),
            $"the tour card's Skip/Next buttons must stay clickable, got {Describe(cardHit)}");

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Hit-tests the laid-out visual tree with the same semantics pointer input uses:
    /// a visual that fails <see cref="InputHitTestFilter"/> prunes its whole subtree,
    /// children are tested topmost-first, and a visual only self-hits where it paints.
    /// Deliberately avoids <c>GetVisualAt</c>: that API reads the server-side
    /// composition readback, which is only written when the process-global headless
    /// render loop successfully renders this window's target. That loop is shared by
    /// every UI test in the assembly, and state leaked by an earlier test (a stale
    /// composition target that throws during the render pass, a wedged pending batch)
    /// permanently prevents later windows from becoming hit-testable — retrying frame
    /// commits cannot recover from that, which made this test flaky on CI.
    /// The contract under test is the visual tree's hit-test-visibility structure,
    /// which layout alone determines.
    /// </summary>
    private static Visual? LayoutHitTest(Visual root, Point point)
        => HitTestTree(root, root, point);

    private static Visual? HitTestTree(Visual root, Visual visual, Point rootPoint)
    {
        if (!InputHitTestFilter(visual))
            return null;

        var toLocal = root.TransformToVisual(visual);
        if (toLocal == null)
            return null;

        var local = rootPoint.Transform(toLocal.Value);
        var localBounds = new Rect(visual.Bounds.Size);
        if (visual.ClipToBounds && !localBounds.Contains(local))
            return null;

        var children = visual.GetVisualChildren().OrderBy(c => c.ZIndex).ToArray();
        for (var i = children.Length - 1; i >= 0; i--)
        {
            var hit = HitTestTree(root, children[i], rootPoint);
            if (hit != null)
                return hit;
        }

        return localBounds.Contains(local) && PaintsItself(visual) ? visual : null;
    }

    /// <summary>
    /// Approximates the composition hit test's self-hit rule (a visual is hit only
    /// where its draw list painted): controls with a background/border brush, shapes
    /// with fill/stroke, and text paint their bounds; background-less panels and
    /// content hosts do not.
    /// </summary>
    private static bool PaintsItself(Visual visual) => visual switch
    {
        TextBlock => true,
        Shape shape => shape.Fill != null || shape.Stroke != null,
        Border border => border.Background != null || border.BorderBrush != null,
        Panel panel => panel.Background != null,
        TemplatedControl templated => templated.Background != null,
        _ => false,
    };

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
