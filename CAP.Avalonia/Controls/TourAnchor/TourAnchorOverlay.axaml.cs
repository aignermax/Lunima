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
    private Control? _revealedControl;
    private (ScrollViewer Scroll, Rect Target, Size Viewport)[] _revealGeometry = [];
    private Control? _cachedTarget;
    private readonly List<Visual> _visibilityAncestors = new();
    private bool _attached;
    private bool _refreshingAnchor;

    /// <summary>Creates the overlay and starts the bounds poll timer.</summary>
    public TourAnchorOverlay()
    {
        InitializeComponent();
        _poll = new DispatcherTimer { Interval = PollInterval };
        _poll.Tick += (_, _) => RefreshAnchor();
        CardHost.Content = Card;
        // Card content and the viewport can change in the same layout as a
        // target switch. Do not leave buttons at the old position until a poll.
        CardHost.SizeChanged += (_, _) => RefreshAnchor();
        RootCanvas.SizeChanged += (_, _) => RefreshAnchor();
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
        {
            _cachedTarget = null;
            _broughtIntoView = null;
            _revealedControl = null;
            _revealGeometry = [];
            _scrolledItem = null;
            _scrolledList = null;
            RefreshAnchor();
        }
        else if (change.Property == IsVisibleProperty)
            UpdatePolling();
        else if (change.Property == DemonstrateProperty || change.Property == TargetItemProperty ||
                 change.Property == TargetCanvasPointProperty || change.Property == TargetPinProperty ||
                 change.Property == TargetCanvasDestinationPointProperty ||
                 change.Property == ConnectionTargetPinProperty)
            RefreshAnchor();
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        foreach (var ancestor in this.GetVisualAncestors())
        {
            _visibilityAncestors.Add(ancestor);
            ancestor.PropertyChanged += OnAncestorPropertyChanged;
        }
        UpdatePolling();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _poll.Stop();
        StopDemonstration();
        ClearOffscreenTarget();
        foreach (var ancestor in _visibilityAncestors)
            ancestor.PropertyChanged -= OnAncestorPropertyChanged;
        _visibilityAncestors.Clear();
        _cachedTarget = null;
        _broughtIntoView = null;
        _revealedControl = null;
        _revealGeometry = [];
        _scrolledItem = null;
        _scrolledList = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnAncestorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == IsVisibleProperty)
            UpdatePolling();
    }

    private void UpdatePolling()
    {
        // Visibility changes can arrive during InitializeComponent, before the timer exists.
        if (_poll == null)
            return;
        if (_attached && IsVisible && _visibilityAncestors.All(v => v.IsVisible))
        {
            _poll.Start();
            RefreshAnchor();
        }
        else
        {
            _poll.Stop();
            StopDemonstration();
            ClearOffscreenTarget();
            _cachedTarget = null;
            _revealedControl = null;
            _revealGeometry = [];
        }
    }

    private void RefreshAnchor()
    {
        if (!_attached || !IsEffectivelyVisible || _refreshingAnchor)
            return;
        _refreshingAnchor = true;
        try { RefreshAnchorCore(); }
        finally { _refreshingAnchor = false; }
    }

    private void RefreshAnchorCore()
    {
        var viewport = RootCanvas.Bounds.Size;
        if (!double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height)
            || viewport.Width <= 0 || viewport.Height <= 0)
            return;
        // Keep the reveal button alive across polling/layout refreshes: briefly
        // hiding it cancels a mouse press before release can produce a click.
        _offscreenCanvas = null;
        _offscreenPoint = null;
        var target = FindTarget();
        target = ResolveItemTarget(target);
        var targetRect = target == null ? default : TargetRectInOverlay(target);
        if (Demonstrate && target != null)
            targetRect = ResolveDemonstrationRect(target, targetRect);

        if (target != null) RevealAfterLayoutChange(target);

        var hasSpotlight = targetRect.Width > 0 && targetRect.Height > 0;
        ApplyDim(viewport, hasSpotlight ? targetRect : default);

        CardHost.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var cardSize = CardHost.DesiredSize;
        if (!double.IsFinite(cardSize.Width) || !double.IsFinite(cardSize.Height))
            return;
        var pos = TourCardPlacement.Place(viewport, targetRect, cardSize, out var side);
        ACanvas.SetLeft(CardHost, pos.X);
        ACanvas.SetTop(CardHost, pos.Y);
        GuideShowTarget.IsVisible = _offscreenPoint != null;
        if (GuideShowTarget.IsVisible)
        {
            GuideShowTarget.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            ACanvas.SetLeft(GuideShowTarget, Math.Max(8, pos.X));
            ACanvas.SetTop(GuideShowTarget, Math.Max(8, pos.Y - GuideShowTarget.DesiredSize.Height - 8));
        }

        UpdateSpotlight(targetRect, hasSpotlight);
        UpdateArrow(targetRect, side, hasSpotlight);
        UpdateDemonstration(hasSpotlight ? targetRect.Center : null);
        if (Demonstrate)
        {
            ApplyDim(viewport, default);
            Arrow.IsVisible = false;
        }
    }

    private void RevealAfterLayoutChange(Control target)
    {
        if (target.Bounds.Width <= 0 || target.Bounds.Height <= 0) return;
        var geometry = new List<(ScrollViewer Scroll, Rect Target, Size Viewport)>();
        var nestedOffsets = default(Vector);
        foreach (var scroll in target.GetVisualAncestors().OfType<ScrollViewer>())
        {
            if (scroll.Content is not Visual content || scroll.Viewport.Width <= 0 || scroll.Viewport.Height <= 0)
                return; // Do not consume the reveal before the scroll content is arranged.
            var origin = target.TranslatePoint(default, content);
            if (origin == null) return;
            // Content coordinates exclude scrolling. Repeated polls and manual
            // offsets therefore cannot cause a scroll fight, but late layout can
            // reveal the action once again after its geometry actually changes.
            geometry.Add((scroll, new Rect(origin.Value + nestedOffsets, target.Bounds.Size), scroll.Viewport));
            // An outer content transform includes every inner scroll offset.
            // Remove those too, so nested lists retain the user's scroll position.
            nestedOffsets += scroll.Offset;
        }
        if (_broughtIntoView == TargetName && ReferenceEquals(_revealedControl, target)
            && _revealGeometry.SequenceEqual(geometry)) return;
        _broughtIntoView = TargetName;
        _revealedControl = target;
        _revealGeometry = geometry.ToArray();
        target.BringIntoView();
    }

    private Control? FindTarget()
    {
        if (string.IsNullOrEmpty(TargetName))
            return null;
        var top = TopLevel.GetTopLevel(this) as Visual;
        if (_cachedTarget is { IsEffectivelyVisible: true } cached && cached.Name == TargetName &&
            TopLevel.GetTopLevel(cached) == top)
            return cached;
        _cachedTarget = top?.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(c => c.Name == TargetName && c.IsEffectivelyVisible);
        return _cachedTarget;
    }

    private Rect TargetRectInOverlay(Control target)
    {
        var origin = target.TranslatePoint(new Point(0, 0), RootCanvas);
        if (origin == null)
            return default;
        var rect = new Rect(origin.Value, target.Bounds.Size);
        if (Demonstrate)
        {
            rect = rect.Intersect(new Rect(RootCanvas.Bounds.Size));
            foreach (var ancestor in target.GetVisualAncestors().Where(v => v.ClipToBounds))
            {
                var clipOrigin = ancestor.TranslatePoint(default, RootCanvas);
                if (clipOrigin == null) return default;
                rect = rect.Intersect(new Rect(clipOrigin.Value, ancestor.Bounds.Size));
            }
            if (rect.Width <= 0 || rect.Height <= 0) return default;
        }
        return rect.Inflate(SpotlightPadding);
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
