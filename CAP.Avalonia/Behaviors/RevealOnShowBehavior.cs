using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace CAP.Avalonia.Behaviors;

/// <summary>
/// Attached behavior that fades and slides a panel into place whenever it is shown —
/// switching the left rail pane, opening a dock, selecting a dock tab. The motion is a
/// single 180 ms ease-out on opacity and translation, so it runs on the compositor and
/// never holds the UI thread.
/// </summary>
/// <example>
/// <code>&lt;DockPanel behaviors:RevealOnShowBehavior.IsEnabled="True" behaviors:RevealOnShowBehavior.FromX="-8"/&gt;</code>
/// </example>
public static class RevealOnShowBehavior
{
    /// <summary>Length of the reveal, matching the design system's Motion.Normal step.</summary>
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(180);

    /// <summary>
    /// Global switch for the reveal motion. Headless screenshot hosts turn it off so a
    /// captured frame never shows a panel half-way through its entrance.
    /// </summary>
    public static bool AnimationsEnabled { get; set; } = true;

    /// <summary>Turns the reveal on for a control.</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(RevealOnShowBehavior));

    /// <summary>Horizontal start offset in pixels (slides from here to 0).</summary>
    public static readonly AttachedProperty<double> FromXProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("FromX", typeof(RevealOnShowBehavior));

    /// <summary>Vertical start offset in pixels (slides from here to 0).</summary>
    public static readonly AttachedProperty<double> FromYProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("FromY", typeof(RevealOnShowBehavior));

    static RevealOnShowBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
    }

    /// <summary>Gets whether the reveal is enabled.</summary>
    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);

    /// <summary>Sets whether the reveal is enabled.</summary>
    public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);

    /// <summary>Gets the horizontal start offset.</summary>
    public static double GetFromX(Control control) => control.GetValue(FromXProperty);

    /// <summary>Sets the horizontal start offset.</summary>
    public static void SetFromX(Control control, double value) => control.SetValue(FromXProperty, value);

    /// <summary>Gets the vertical start offset.</summary>
    public static double GetFromY(Control control) => control.GetValue(FromYProperty);

    /// <summary>Sets the vertical start offset.</summary>
    public static void SetFromY(Control control, double value) => control.SetValue(FromYProperty, value);

    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            control.PropertyChanged += OnControlPropertyChanged;
            control.AttachedToVisualTree += OnAttachedToVisualTree;
        }
        else
        {
            control.PropertyChanged -= OnControlPropertyChanged;
            control.AttachedToVisualTree -= OnAttachedToVisualTree;
        }
    }

    private static void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty && e.NewValue is true && sender is Control control)
            Reveal(control);
    }

    private static void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control { IsVisible: true } control)
            Reveal(control);
    }

    private static void Reveal(Control control)
    {
        if (!AnimationsEnabled)
            return;
        control.RenderTransform ??= new TranslateTransform();
        _ = CreateAnimation(GetFromX(control), GetFromY(control)).RunAsync(control);
    }

    private static Animation CreateAnimation(double fromX, double fromY) => new()
    {
        Duration = Duration,
        Easing = new CubicEaseOut(),
        Children =
        {
            new KeyFrame
            {
                Cue = new Cue(0),
                Setters =
                {
                    new Setter(Visual.OpacityProperty, 0d),
                    new Setter(TranslateTransform.XProperty, fromX),
                    new Setter(TranslateTransform.YProperty, fromY),
                },
            },
            new KeyFrame
            {
                Cue = new Cue(1),
                Setters =
                {
                    new Setter(Visual.OpacityProperty, 1d),
                    new Setter(TranslateTransform.XProperty, 0d),
                    new Setter(TranslateTransform.YProperty, 0d),
                },
            },
        },
    };
}
