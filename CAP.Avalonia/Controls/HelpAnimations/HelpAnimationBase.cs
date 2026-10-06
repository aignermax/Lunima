using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace CAP.Avalonia.Controls.HelpAnimations;

/// <summary>
/// Base class for the small looping diagrams inside (?) help flyouts (#1152).
/// The whole animation is a pure function of <see cref="Progress"/> (0 = first frame,
/// 1 = last frame), which gives two modes from one code path: with
/// <see cref="AutoPlay"/> = true (default) a lightweight dispatcher timer advances the
/// loop while the control is visible; tests set AutoPlay = false and scrub
/// <see cref="Progress"/> directly, so headless screenshot captures render an exact,
/// deterministic frame. Per-tick work is a handful of Canvas/Opacity property sets —
/// microseconds — and the timer only runs while attached to the visual tree, so a
/// closed flyout consumes nothing and the UI thread is never blocked.
/// </summary>
public abstract class HelpAnimationBase : UserControl
{
    /// <summary>Position inside one loop: 0 (first frame) … 1 (last frame). Clamped to [0, 1].</summary>
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<HelpAnimationBase, double>(
            nameof(Progress), coerce: (_, value) => Math.Clamp(value, 0.0, 1.0));

    /// <summary>True (default): advance <see cref="Progress"/> automatically while visible.</summary>
    public static readonly StyledProperty<bool> AutoPlayProperty =
        AvaloniaProperty.Register<HelpAnimationBase, bool>(nameof(AutoPlay), defaultValue: true);

    /// <summary>Wall-clock length of one animation loop.</summary>
    public static readonly StyledProperty<TimeSpan> LoopDurationProperty =
        AvaloniaProperty.Register<HelpAnimationBase, TimeSpan>(nameof(LoopDuration), TimeSpan.FromSeconds(3));

    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(33);

    private readonly DispatcherTimer _timer;
    private bool _isAttached;

    /// <summary>Initializes the timer (stopped until the control is attached and AutoPlay is on).</summary>
    protected HelpAnimationBase()
    {
        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += (_, _) => Advance();
    }

    /// <summary>Position inside one loop: 0 (first frame) … 1 (last frame).</summary>
    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>True (default): advance <see cref="Progress"/> automatically while visible.</summary>
    public bool AutoPlay
    {
        get => GetValue(AutoPlayProperty);
        set => SetValue(AutoPlayProperty, value);
    }

    /// <summary>Wall-clock length of one animation loop.</summary>
    public TimeSpan LoopDuration
    {
        get => GetValue(LoopDurationProperty);
        set => SetValue(LoopDurationProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ProgressProperty)
            RenderFrame(change.GetNewValue<double>());
        else if (change.Property == AutoPlayProperty)
            UpdateTimer();
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        UpdateTimer();
        RenderFrame(Progress);
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Draws the frame for <paramref name="progress"/> (0 = first, 1 = last).</summary>
    protected abstract void RenderFrame(double progress);

    /// <summary>
    /// Linear 0→1 ramp across a cue window: 0 at or before <paramref name="start"/>,
    /// 1 at or after <paramref name="end"/>. Used to fade/move elements in staggered windows.
    /// </summary>
    protected static double Ramp(double progress, double start, double end)
    {
        if (progress <= start)
            return 0;
        if (progress >= end)
            return 1;
        return (progress - start) / (end - start);
    }

    private void UpdateTimer()
    {
        if (AutoPlay && _isAttached)
            _timer.Start();
        else
            _timer.Stop();
    }

    private void Advance()
    {
        var duration = LoopDuration;
        if (duration <= TimeSpan.Zero)
            return;
        Progress = (Progress + TickInterval / duration) % 1.0;
    }
}
