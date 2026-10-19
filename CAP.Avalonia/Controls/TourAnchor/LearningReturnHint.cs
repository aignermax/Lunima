using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Behaviors;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Onboarding.LearningPath;
using CAP.Avalonia.Views;

namespace CAP.Avalonia.Controls.TourAnchor;

/// <summary>A short, non-interactive reminder of where a paused learning path resumes.</summary>
public sealed class LearningReturnHint : Control
{
    public static readonly StyledProperty<LearningPathViewModel?> ModelProperty =
        AvaloniaProperty.Register<LearningReturnHint, LearningPathViewModel?>(nameof(Model));

    private const double TravelSeconds = 0.8;
    private const double PulseSeconds = 0.45;
    private const double LabelSeconds = 3;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private LearningPathViewModel? _subscribedModel;
    private Window? _window;
    private Control? _target;
    private HomeView? _home;
    private Point _source;
    private Point _destination;
    private bool _attached;
    private bool _reducedMotion;
    private IBrush? _success;
    private IBrush? _panel;
    private Pen? _outline;
    private FormattedText? _label;

    public LearningReturnHint()
    {
        IsHitTestVisible = false;
        Focusable = false;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
    }

    public LearningPathViewModel? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    /// <summary>Whether the one-shot return cue is currently visible.</summary>
    public bool IsPlaying => _clock.IsRunning;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ModelProperty)
        {
            Stop();
            Subscribe(_attached ? Model : null);
        }
        else if (change.Property == IsVisibleProperty && !IsVisible)
            Stop();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Subscribe(Model);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        Stop();
        Subscribe(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void Subscribe(LearningPathViewModel? model)
    {
        if (_subscribedModel != null)
        {
            _subscribedModel.Pausing -= Begin;
            _subscribedModel.PropertyChanged -= OnModelChanged;
        }
        _subscribedModel = model;
        if (model != null)
        {
            model.Pausing += Begin;
            model.PropertyChanged += OnModelChanged;
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if ((string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(LearningPathViewModel.IsVisible))
            && Model?.IsVisible == true)
            Stop();
    }

    private void Begin()
    {
        Stop();
        if (!_attached || !IsEffectivelyVisible || TopLevel.GetTopLevel(this) is not Window window)
            return;

        // Pausing is raised before hiding the panel, so the source is still measurable.
        var controls = window.GetVisualDescendants().OfType<Control>().ToArray();
        var source = controls.FirstOrDefault(c => c.Name == "LearningPathCloseButton" && c.IsEffectivelyVisible);
        var target = controls.FirstOrDefault(c => c.Name == "ResumeVisualGuideButton" && c.IsEffectivelyVisible);
        var home = controls.OfType<HomeView>().FirstOrDefault();
        if (source == null || target == null || home?.IsEffectivelyVisible == true ||
            source.Bounds.Width <= 0 || target.Bounds.Width <= 0 ||
            source.TranslatePoint(new Point(source.Bounds.Width / 2, source.Bounds.Height / 2), this) is not { } from ||
            target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), this) is not { } to)
            return;

        _success = Resource<IBrush>("Brush.Success");
        _panel = Resource<IBrush>("Brush.Surface.Panel");
        var foreground = Resource<IBrush>("Brush.Text.Primary");
        if (_success == null || _panel == null || foreground == null) return;
        _outline = new Pen(_success, 1.5);
        _label = new FormattedText(LocalizationService.Instance.Translate("LearningPath.ResumeHere"),
            CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(Resource<FontFamily>("Font.Ui") ?? FontFamily.Default, weight: FontWeight.SemiBold),
            12, foreground) { MaxTextWidth = Math.Max(1, Math.Min(240, Bounds.Width - 32)) };
        _window = window;
        _target = target;
        _home = home;
        _source = from;
        _destination = to;
        _reducedMotion = MotionPreferences.ReduceMotion;
        _timer.Interval = TimeSpan.FromMilliseconds(_reducedMotion ? 100 : 16);
        _clock.Restart();
        _timer.Start();
        InvalidateVisual();
    }

    private T? Resource<T>(string key) where T : class =>
        this.TryFindResource(key, out var value) ? value as T : null;

    private void OnTick(object? sender, EventArgs e)
    {
        var duration = (_reducedMotion ? 0 : TravelSeconds + PulseSeconds) + LabelSeconds;
        if (!_attached || !IsEffectivelyVisible || Model?.IsVisible != false ||
            _clock.Elapsed.TotalSeconds >= duration || _home?.IsEffectivelyVisible == true ||
            _window == null || TopLevel.GetTopLevel(this) != _window ||
            _target is not { IsEffectivelyVisible: true } target || TopLevel.GetTopLevel(target) != _window ||
            target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), this) is not { } point)
        {
            Stop();
            return;
        }
        _destination = point;
        InvalidateVisual();
    }

    private void Stop()
    {
        _timer?.Stop();
        _clock.Reset();
        _window = null;
        _target = null;
        _home = null;
        _label = null;
        _outline = null;
        _success = null;
        _panel = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!_clock.IsRunning || _label == null || _outline == null || _success == null) return;
        var elapsed = _clock.Elapsed.TotalSeconds;
        var travel = _reducedMotion ? 1 : Math.Clamp(elapsed / TravelSeconds, 0, 1);
        var eased = travel * travel * (3 - 2 * travel);
        var point = new Point(_source.X + (_destination.X - _source.X) * eased,
            _source.Y + (_destination.Y - _source.Y) * eased - Math.Sin(travel * Math.PI) * 32);

        if (travel < 1)
        {
            context.DrawEllipse(_success, null, point, 5, 5);
            return;
        }

        // A visual click only: the real toolbar button never receives input or changes state.
        var pulse = _reducedMotion ? 1 : Math.Clamp((elapsed - TravelSeconds) / PulseSeconds, 0, 1);
        var radius = _reducedMotion ? 15 : 15 + Math.Sin(pulse * Math.PI) * 7;
        context.DrawEllipse(null, _outline, _destination, radius, radius);
        if (pulse < 1) context.DrawEllipse(_success, null, _destination, 4 * (1 - pulse), 4 * (1 - pulse));

        var width = _label.Width + 20;
        var height = _label.Height + 16;
        var x = Math.Clamp(_destination.X - width / 2, 8, Math.Max(8, Bounds.Width - width - 8));
        var y = _destination.Y + 26;
        if (y + height > Bounds.Height - 8) y = Math.Max(8, _destination.Y - height - 26);
        context.DrawRectangle(_panel, _outline, new Rect(x, y, width, height), 6, 6);
        context.DrawText(_label, new Point(x + 10, y + 8));
    }
}
