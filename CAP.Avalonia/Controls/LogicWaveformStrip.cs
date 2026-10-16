using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Input;
using System.Windows.Input;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.Waveform;

namespace CAP.Avalonia.Controls;

/// <summary>
/// The Logic panel's waveform strip (issue #1129, rung 5 visualizer): a
/// fixed-height, interactive view drawing the event timeline as per-signal 0/1
/// traces — one lane per named signal, vertical lines at the clock boundaries, a
/// cursor at the replayed instant. The control is a thin Avalonia host for
/// <see cref="LogicWaveformRenderer"/>: its <see cref="Waveform"/> property carries
/// the already-mapped model, the height follows the lane count, and every model
/// change repaints. Hover identifies a lane; clicking its trace seeks replay.
/// </summary>
public class LogicWaveformStrip : Control
{
    /// <summary>The waveform model to draw; null draws nothing.</summary>
    public static readonly StyledProperty<LogicWaveformModel?> WaveformProperty =
        AvaloniaProperty.Register<LogicWaveformStrip, LogicWaveformModel?>(nameof(Waveform));

    /// <summary>Seeks the existing event timeline to a time in picoseconds.</summary>
    public static readonly StyledProperty<ICommand?> SeekCommandProperty =
        AvaloniaProperty.Register<LogicWaveformStrip, ICommand?>(nameof(SeekCommand));

    /// <summary>Command receiving the clicked time in picoseconds.</summary>
    public ICommand? SeekCommand
    {
        get => GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

    internal int HoveredLaneIndex { get; private set; } = -1;
    private Point? _pointerPosition;

    static LogicWaveformStrip()
    {
        AffectsRender<LogicWaveformStrip>(WaveformProperty);
        AffectsMeasure<LogicWaveformStrip>(WaveformProperty);
    }

    /// <summary>The waveform model to draw; null draws nothing.</summary>
    public LogicWaveformModel? Waveform
    {
        get => GetValue(WaveformProperty);
        set => SetValue(WaveformProperty, value);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _pointerPosition = e.GetPosition(this);
        UpdateHover();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointerPosition = null;
        UpdateHover();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WaveformProperty)
            UpdateHover();
    }

    private int LaneAt(Point point)
    {
        if (Waveform == null || point.X < 0 || point.X > Bounds.Width ||
            point.Y < LogicWaveformRenderer.HeaderHeight)
            return -1;
        var index = (int)((point.Y - LogicWaveformRenderer.HeaderHeight) / LogicWaveformRenderer.LaneHeight);
        return index < Waveform.Lanes.Count ? index : -1;
    }

    private void UpdateHover()
    {
        var index = _pointerPosition is { } point ? LaneAt(point) : -1;
        ToolTip.SetTip(this, index >= 0 ? Waveform!.Lanes[index].SignalName : null);
        if (index == HoveredLaneIndex)
            return;
        HoveredLaneIndex = index;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetPosition(this);
        var width = LogicWaveformRenderer.TraceWidth(Bounds.Width);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || LaneAt(point) < 0 ||
            width <= 0 || point.X < LogicWaveformRenderer.TraceLeft ||
            point.X > LogicWaveformRenderer.TraceLeft + width)
            return;
        var model = Waveform!;
        var fraction = (point.X - LogicWaveformRenderer.TraceLeft) / width;
        var time = model.StartTimePicoseconds + fraction * (model.EndTimePicoseconds - model.StartTimePicoseconds);
        if (SeekCommand?.CanExecute(time) == true)
        {
            SeekCommand.Execute(time);
            e.Handled = true;
        }
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        return new Size(width, LogicWaveformRenderer.DesiredHeight(Waveform));
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (Waveform != null)
            LogicWaveformRenderer.Render(context, Waveform, Bounds.Size, HoveredLaneIndex);
    }
}
