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
    public static readonly StyledProperty<Point?> TargetCanvasDestinationPointProperty =
        AvaloniaProperty.Register<TourAnchorOverlay, Point?>(nameof(TargetCanvasDestinationPoint));
    public static readonly StyledProperty<PhysicalPin?> TargetPinProperty =
        AvaloniaProperty.Register<TourAnchorOverlay, PhysicalPin?>(nameof(TargetPin));
    public static readonly StyledProperty<PhysicalPin?> ConnectionTargetPinProperty =
        AvaloniaProperty.Register<TourAnchorOverlay, PhysicalPin?>(nameof(ConnectionTargetPin));

    public bool Demonstrate { get => GetValue(DemonstrateProperty); set => SetValue(DemonstrateProperty, value); }
    public object? TargetItem { get => GetValue(TargetItemProperty); set => SetValue(TargetItemProperty, value); }
    public Point? TargetCanvasPoint { get => GetValue(TargetCanvasPointProperty); set => SetValue(TargetCanvasPointProperty, value); }
    public Point? TargetCanvasDestinationPoint { get => GetValue(TargetCanvasDestinationPointProperty); set => SetValue(TargetCanvasDestinationPointProperty, value); }
    public PhysicalPin? TargetPin { get => GetValue(TargetPinProperty); set => SetValue(TargetPinProperty, value); }
    public PhysicalPin? ConnectionTargetPin { get => GetValue(ConnectionTargetPinProperty); set => SetValue(ConnectionTargetPinProperty, value); }

    private DispatcherTimer? _pointerTimer;
    private readonly Stopwatch _pointerClock = new();
    private Point? _pointerTarget;
    private Point? _pointerDestination;
    private object? _scrolledItem;
    private ListBox? _scrolledList;
    private DesignCanvas? _offscreenCanvas;
    private Point? _offscreenPoint;

    private void ClearOffscreenTarget()
    {
        _offscreenCanvas = null;
        _offscreenPoint = null;
        if (GuideShowTarget != null) GuideShowTarget.IsVisible = false;
    }

    private void ShowTarget_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_offscreenCanvas is not { ViewModel: { } vm } canvas || _offscreenPoint is not { } point)
            return;
        Point? startPoint = TargetCanvasPoint, endPoint = TargetCanvasDestinationPoint;
        if (TargetPin is { } startPin)
        {
            var start = startPin.GetAbsolutePosition();
            startPoint = new Point(start.x, start.y);
            endPoint = null;
        }
        if (ConnectionTargetPin is { } endPin)
        {
            var end = endPin.GetAbsolutePosition();
            endPoint = new Point(end.x, end.y);
        }
        if (startPoint is { } from && endPoint is { } to)
        {
            point = new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
            var fit = Math.Min(Math.Max(1, canvas.Bounds.Width - 96) / Math.Max(1, Math.Abs(to.X - from.X)),
                Math.Max(1, canvas.Bounds.Height - 96) / Math.Max(1, Math.Abs(to.Y - from.Y)));
            canvas.SetCurrentValue(DesignCanvas.ZoomProperty, Math.Min(canvas.Zoom, fit));
        }
        // Only the viewport changes, and only after the user requests it.
        vm.PanX = canvas.Bounds.Width / 2 - point.X * canvas.Zoom;
        vm.PanY = canvas.Bounds.Height / 2 - point.Y * canvas.Zoom;
        RefreshAnchor();
    }

    private Control? ResolveItemTarget(Control? target)
    {
        if (!Demonstrate || TargetItem == null)
            return target;
        if (target is not ListBox list || !list.Items.Contains(TargetItem))
            return null;
        if (!ReferenceEquals(_scrolledItem, TargetItem) || !ReferenceEquals(_scrolledList, list))
        {
            list.ScrollIntoView(TargetItem);
            _scrolledItem = TargetItem;
            _scrolledList = list;
        }
        return list.ContainerFromItem(TargetItem);
    }

    private Rect ResolveDemonstrationRect(Control target, Rect fallback)
    {
        _pointerDestination = null;
        if (target is not DesignCanvas canvas || canvas.ViewModel == null)
            return fallback;
        var point = TargetCanvasPoint;
        var destination = TargetCanvasDestinationPoint;
        if (TargetPin is { } first)
        {
            destination = null;
            var pin = canvas.InteractionState.ConnectionDragStartPin == first
                ? ConnectionTargetPin ?? first : first;
            var position = pin.GetAbsolutePosition();
            point = new Point(position.x, position.y);
            if (canvas.InteractionState.ConnectionDragStartPin == null && ConnectionTargetPin is { } endPin)
            {
                var end = endPin.GetAbsolutePosition();
                destination = new Point(end.x, end.y);
            }
        }
        if (destination is { } endpoint)
        {
            var endLocal = new Point(endpoint.X * canvas.Zoom + canvas.ViewModel.PanX,
                endpoint.Y * canvas.Zoom + canvas.ViewModel.PanY);
            if (new Rect(canvas.Bounds.Size).Contains(endLocal))
                _pointerDestination = canvas.TranslatePoint(endLocal, RootCanvas);
            else
            {
                _offscreenCanvas = canvas;
                _offscreenPoint = endpoint;
            }
        }
        if (point == null)
            return default; // A generic canvas centre would demonstrate an unrelated action.
        var local = new Point(point.Value.X * canvas.Zoom + canvas.ViewModel.PanX,
            point.Value.Y * canvas.Zoom + canvas.ViewModel.PanY);
        if (!new Rect(canvas.Bounds.Size).Contains(local))
        {
            _offscreenCanvas = canvas;
            _offscreenPoint = point;
            return default;
        }
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
        GuideTrail.IsVisible = GuideDestination.IsVisible = _pointerDestination != null;
        if (_pointerDestination is { } destination)
        {
            GuideTrail.StartPoint = target.Value;
            GuideTrail.EndPoint = destination;
            ACanvas.SetLeft(GuideDestination, destination.X - 7);
            ACanvas.SetTop(GuideDestination, destination.Y - 7);
        }
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
        var pose = _pointerDestination is { } destination
            ? GuidePointerMotion.SampleConnection(target, destination, _pointerClock.Elapsed.TotalSeconds, MotionPreferences.ReduceMotion)
            : GuidePointerMotion.Sample(target, _pointerClock.Elapsed.TotalSeconds, MotionPreferences.ReduceMotion);
        ACanvas.SetLeft(GuidePointer, pose.Position.X);
        ACanvas.SetTop(GuidePointer, pose.Position.Y);
        GuideClick.Width = GuideClick.Height = pose.ClickRadius * 2;
        GuideClick.Opacity = pose.ClickOpacity;
        ACanvas.SetLeft(GuideClick, pose.Position.X - pose.ClickRadius);
        ACanvas.SetTop(GuideClick, pose.Position.Y - pose.ClickRadius);
    }

    private void StopDemonstration()
    {
        _pointerTimer?.Stop();
        _pointerClock.Stop();
        _pointerTarget = null;
        _pointerDestination = null;
        if (GuidePointer != null) GuidePointer.IsVisible = false;
        if (GuideClick != null) GuideClick.IsVisible = false;
        if (GuideTrail != null) GuideTrail.IsVisible = false;
        if (GuideDestination != null) GuideDestination.IsVisible = false;
    }
}
