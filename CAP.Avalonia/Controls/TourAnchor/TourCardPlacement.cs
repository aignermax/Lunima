using Avalonia;

namespace CAP.Avalonia.Controls.TourAnchor;

/// <summary>Which side of the spotlight target the tour card was placed on.</summary>
public enum TourCardSide
{
    /// <summary>No target — card floats bottom-centre (legacy tour card position).</summary>
    FloatingBottomCenter,
    /// <summary>Card sits below the target, arrow points up.</summary>
    Bottom,
    /// <summary>Card sits above the target, arrow points down.</summary>
    Top,
    /// <summary>Card sits right of the target, arrow points left.</summary>
    Right,
    /// <summary>Card sits left of the target, arrow points right.</summary>
    Left,
}

/// <summary>
/// Pure geometry for the guided-tour overlay (#1167): decides where the tour card
/// goes relative to the spotlighted target rect so it neither covers the target
/// nor leaves the viewport. Preference order: below, above, right, left; when the
/// target rect is empty the card falls back to the legacy bottom-centre position.
/// </summary>
public static class TourCardPlacement
{
    /// <summary>Gap between the spotlight outline and the card, in device-independent pixels.</summary>
    public const double CardGap = 14;

    /// <summary>Minimum margin the card keeps to every viewport edge.</summary>
    public const double ViewportMargin = 12;

    /// <summary>
    /// Computes the top-left position of a card of <paramref name="card"/> size inside
    /// a viewport of <paramref name="viewport"/> size, anchored to <paramref name="target"/>.
    /// </summary>
    public static Point Place(Size viewport, Rect target, Size card, out TourCardSide side)
    {
        if (target.Width <= 0 || target.Height <= 0)
        {
            side = TourCardSide.FloatingBottomCenter;
            return new Point(
                (viewport.Width - card.Width) / 2,
                viewport.Height - card.Height - 28);
        }

        double centerX = Clamp(target.Center.X - card.Width / 2, ViewportMargin, viewport.Width - card.Width - ViewportMargin);
        double centerY = Clamp(target.Center.Y - card.Height / 2, ViewportMargin, viewport.Height - card.Height - ViewportMargin);

        double below = target.Bottom + CardGap;
        if (below + card.Height <= viewport.Height - ViewportMargin)
        {
            side = TourCardSide.Bottom;
            return new Point(centerX, below);
        }

        double above = target.Top - CardGap - card.Height;
        if (above >= ViewportMargin)
        {
            side = TourCardSide.Top;
            return new Point(centerX, above);
        }

        double right = target.Right + CardGap;
        if (right + card.Width <= viewport.Width - ViewportMargin)
        {
            side = TourCardSide.Right;
            return new Point(right, centerY);
        }

        double left = target.Left - CardGap - card.Width;
        if (left >= ViewportMargin)
        {
            side = TourCardSide.Left;
            return new Point(left, centerY);
        }

        // Target fills the viewport — overlap is unavoidable; centre the card.
        side = TourCardSide.Bottom;
        return new Point(centerX, centerY);
    }

    private static double Clamp(double value, double min, double max)
        => max < min ? min : Math.Min(Math.Max(value, min), max);
}
