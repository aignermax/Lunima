using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CAP.Avalonia.Behaviors;
using CAP_Core.Components.Core;
using System.Diagnostics;
using ACanvas = global::Avalonia.Controls.Canvas;

namespace CAP.Avalonia.Controls.TourAnchor;

public partial class TourAnchorOverlay
{
    public static readonly StyledProperty<bool> DemonstrateProperty =
        AvaloniaProperty.Register<TourAnchorOverlay, bool>(nameof(Demonstrate));
    public static readonly StyledProperty<object?> TargetItemProperty =
        AvaloniaProperty.Register<TourAnchorOverlay, object?>(nameof(TargetItem));
    public static readonly StyledProperty<Point?> TargetCanvasPointProperty =
        AvaloniaProperty.Register<TourAnchorOverlay, Point?>(nameof(TargetCanvasPoint));
    public static readonly StyledProperty<PhysicalPin?> TargetPinProperty =
        AvaloniaProperty.Register<TourAnchorOverlay, PhysicalPin?>(nameof(TargetPin));
    public static readonly StyledProperty<PhysicalPin?> ConnectionTargetPinProperty =
        AvaloniaProperty.Register<TourAnchorOverlay, PhysicalPin?>(nameof(ConnectionTargetPin));

    public bool Demonstrate { get => GetValue(DemonstrateProperty); set => SetValue(DemonstrateProperty, value); }
    public object? TargetItem { get => GetValue(TargetItemProperty); set => SetValue(TargetItemProperty, value); }
    public Point? TargetCanvasPoint { get => GetValue(TargetCanvasPointProperty); set => SetValue(TargetCanvasPointProperty, value); }
    public PhysicalPin? TargetPin { get => GetValue(TargetPinProperty); set => SetValue(TargetPinProperty, value); }
    public PhysicalPin? ConnectionTargetPin { get => GetValue(ConnectionTargetPinProperty); set => SetValue(ConnectionTargetPinProperty, value); }

    private DispatcherTimer? _pointerTimer;
    private readonly Stopwatch _pointerClock = new();
    private Point? _pointerTarget;
    private object? _scrolledItem;

    private Control? ResolveItemTarget(Control? target)
    {
        if (!Demonstrate || TargetItem == null)
            return target;
        if (target is not ListBox list || !list.Items.Contains(TargetItem))
            return null;
        if (!ReferenceEquals(_scrolledItem, TargetItem))
        {
            list.ScrollIntoView(TargetItem);
            _scrolledItem = TargetItem;
        }
        return list.ContainerFromItem(TargetItem);
    }

    private Rect ResolveDemonstrationRect(Control target, Rect fallback)
    {
        if (target is not DesignCanvas canvas || canvas.ViewModel == null)
            return fallback;
        var point = TargetCanvasPoint;
        if (TargetPin is { } first)
        {
            var pin = canvas.InteractionState.ConnectionDragStartPin == first
                ? ConnectionTargetPin ?? first : first;
            var position = pin.GetAbsolutePosition();
            point = new Point(position.x, position.y);
        }
        if (point == null)
            return default; // A generic canvas centre would demonstrate an unrelated action.
        var local = new Point(point.Value.X * canvas.Zoom + canvas.ViewModel.PanX,
            point.Value.Y * canvas.Zoom + canvas.ViewModel.PanY);
        if (!new Rect(canvas.Bounds.Size).Contains(local))
            return default;
        var translated = canvas.TranslatePoint(local, RootCanvas);
        return translated is { } actual ? new Rect(actual.X - 8, actual.Y - 8, 16, 16) : default;
    }

    private void UpdateDemonstration(Point? target)
    {
        if (!Demonstrate || target == null || !_attached || !IsEffectivelyVisible)
        {
            StopDemonstration();
            return;
        }
        if (_pointerTarget != target)
        {
            _pointerTarget = target;
            _pointerClock.Restart();
        }
        GuidePointer.IsVisible = true;
        GuideClick.IsVisible = true;
        PaintPointer();
        if (MotionPreferences.ReduceMotion)
        {
            _pointerTimer?.Stop();
            return;
        }
        if (_pointerTimer == null)
        {
            _pointerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _pointerTimer.Tick += (_, _) => PaintPointer();
        }
        _pointerTimer.Start();
    }

    private void PaintPointer()
    {
        if (_pointerTarget is not { } target)
            return;
        var pose = GuidePointerMotion.Sample(target, _pointerClock.Elapsed.TotalSeconds, MotionPreferences.ReduceMotion);
        ACanvas.SetLeft(GuidePointer, pose.Position.X);
        ACanvas.SetTop(GuidePointer, pose.Position.Y);
        GuideClick.Width = GuideClick.Height = pose.ClickRadius * 2;
        GuideClick.Opacity = pose.ClickOpacity;
        ACanvas.SetLeft(GuideClick, target.X - pose.ClickRadius);
        ACanvas.SetTop(GuideClick, target.Y - pose.ClickRadius);
    }

    private void StopDemonstration()
    {
        _pointerTimer?.Stop();
        _pointerClock.Stop();
        _pointerTarget = null;
        _scrolledItem = null;
        if (GuidePointer != null) GuidePointer.IsVisible = false;
        if (GuideClick != null) GuideClick.IsVisible = false;
    }
}
