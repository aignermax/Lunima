using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ACanvas = global::Avalonia.Controls.Canvas;

namespace CAP.Avalonia.Controls.TourAnchor;

/// <summary>
/// Anchors a guided-tour card to its target control (#1167): dims the rest of the
/// window, pulses an outline around the target, points an arrow at it and places
/// the card on the side with the most room. When the target is scrolled out of
/// view it is brought into view first; when no target is named (or it cannot be
/// found) the card falls back to the legacy bottom-centre position with no dim.
/// </summary>
public partial class TourAnchorOverlay : UserControl
{
    /// <summary>Identifies the <see cref="CardProperty"/>.</summary>
    public static readonly StyledProperty<object?> CardProperty =
        AvaloniaProperty.Register<TourAnchorOverlay, object?>(nameof(Card));

    /// <summary>Identifies the <see cref="TargetNameProperty"/>.</summary>
    public static readonly StyledProperty<string?> TargetNameProperty =
        AvaloniaProperty.Register<TourAnchorOverlay, string?>(nameof(TargetName));

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(150);
    private const double SpotlightPadding = 6;

    private readonly DispatcherTimer _poll;
    private string? _broughtIntoView;

    /// <summary>Creates the overlay and starts the bounds poll timer.</summary>
    public TourAnchorOverlay()
    {
        InitializeComponent();
        _poll = new DispatcherTimer { Interval = PollInterval };
        _poll.Tick += (_, _) => RefreshAnchor();
        CardHost.Content = Card;
    }

    /// <summary>The tour card content placed next to the target.</summary>
    public object? Card
    {
        get => GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }

    /// <summary><c>x:Name</c> of the control the current step anchors to; null/empty = floating card.</summary>
    public string? TargetName
    {
        get => GetValue(TargetNameProperty);
        set => SetValue(TargetNameProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CardProperty)
            CardHost.Content = change.NewValue;
        else if (change.Property == TargetNameProperty)
            RefreshAnchor();
        else if (change.Property == IsVisibleProperty)
            RefreshAnchor();
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _poll.Start();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _poll.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void RefreshAnchor()
    {
        if (!IsVisible)
            return;

        var viewport = RootCanvas.Bounds.Size;
        var target = FindTarget();
        var targetRect = target == null ? default : TargetRectInOverlay(target);

        if (target != null && _broughtIntoView != TargetName)
        {
            target.BringIntoView();
            _broughtIntoView = TargetName;
        }

        var hasSpotlight = targetRect.Width > 0 && targetRect.Height > 0;
        ApplyDim(viewport, hasSpotlight ? targetRect : default);

        CardHost.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var cardSize = CardHost.DesiredSize;
        var pos = TourCardPlacement.Place(viewport, targetRect, cardSize, out var side);
        ACanvas.SetLeft(CardHost, pos.X);
        ACanvas.SetTop(CardHost, pos.Y);

        UpdateSpotlight(targetRect, hasSpotlight);
        UpdateArrow(targetRect, side, hasSpotlight);
    }

    private Control? FindTarget()
    {
        if (string.IsNullOrEmpty(TargetName))
            return null;
        var top = TopLevel.GetTopLevel(this) as Visual;
        return top?.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(c => c.Name == TargetName && c.IsEffectivelyVisible);
    }

    private Rect TargetRectInOverlay(Control target)
    {
        var origin = target.TranslatePoint(new Point(0, 0), RootCanvas);
        if (origin == null)
            return default;
        return new Rect(origin.Value, target.Bounds.Size).Inflate(SpotlightPadding);
    }

    private void ApplyDim(Size viewport, Rect hole)
    {
        var show = hole.Width > 0;
        SetRect(DimTop, show ? new Rect(0, 0, viewport.Width, Math.Max(0, hole.Top)) : default);
        SetRect(DimBottom, show ? new Rect(0, hole.Bottom, viewport.Width, Math.Max(0, viewport.Height - hole.Bottom)) : default);
        SetRect(DimLeft, show ? new Rect(0, hole.Top, Math.Max(0, hole.Left), hole.Height) : default);
        SetRect(DimRight, show ? new Rect(hole.Right, hole.Top, Math.Max(0, viewport.Width - hole.Right), hole.Height) : default);
    }

    private static void SetRect(global::Avalonia.Controls.Shapes.Rectangle rect, Rect bounds)
    {
        rect.IsVisible = bounds.Width > 0 && bounds.Height > 0;
        ACanvas.SetLeft(rect, bounds.X);
        ACanvas.SetTop(rect, bounds.Y);
        rect.Width = Math.Max(0, bounds.Width);
        rect.Height = Math.Max(0, bounds.Height);
    }

    private void UpdateSpotlight(Rect targetRect, bool visible)
    {
        Spotlight.IsVisible = visible;
        if (!visible)
            return;
        ACanvas.SetLeft(Spotlight, targetRect.X);
        ACanvas.SetTop(Spotlight, targetRect.Y);
        Spotlight.Width = targetRect.Width;
        Spotlight.Height = targetRect.Height;
    }

    private void UpdateArrow(Rect targetRect, TourCardSide side, bool visible)
    {
        Arrow.IsVisible = visible && side != TourCardSide.FloatingBottomCenter;
        if (!Arrow.IsVisible)
            return;

        // Triangle points right at 0°; rotate to aim from the card at the target.
        var (x, y, angle) = side switch
        {
            TourCardSide.Bottom => (targetRect.Center.X - 8, targetRect.Bottom + 2, -90.0),
            TourCardSide.Top => (targetRect.Center.X - 8, targetRect.Top - 18, 90.0),
            TourCardSide.Right => (targetRect.Right + 2, targetRect.Center.Y - 8, 180.0),
            _ => (targetRect.Left - 18, targetRect.Center.Y - 8, 0.0),
        };
        ACanvas.SetLeft(Arrow, x);
        ACanvas.SetTop(Arrow, y);
        Arrow.RenderTransform = new RotateTransform(angle);
    }
}
