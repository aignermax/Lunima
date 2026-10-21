using CAP.Avalonia.Services.GdsImport;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.LayerPicker;

namespace CAP.Avalonia.Controls.GdsImport;

/// <summary>Native GDS review surface with layer selection and editable local-cell ports.</summary>
public sealed class GdsReviewPreview : Control
{
    private const double PaddingPixels = 24;
    private const double PortRadius = 6;
    private const double PickRadius = 10;
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#11191D"));
    private static readonly IBrush PortBrush = new SolidColorBrush(Color.Parse("#53EEE0"));
    private GdsReviewGeometryCache? _cache;
    private GdsReviewHierarchyCache? _hierarchy;
    private double _zoom = 1;
    private Vector _pan;
    private Point? _panStart;
    private int _dragPort = -1;
    private IPointer? _capturedPointer;
    private GdsBoundingBox? _focusBounds;
    /// <summary>The GDS geometry and optional complete hierarchy to review.</summary>
    public static readonly StyledProperty<GdsLayerGeometryScene?> SceneProperty =
        AvaloniaProperty.Register<GdsReviewPreview, GdsLayerGeometryScene?>(nameof(Scene));
    /// <summary>The emphasized source layer.</summary>
    public static readonly StyledProperty<GdsLayerPair?> HighlightLayerProperty =
        AvaloniaProperty.Register<GdsReviewPreview, GdsLayerPair?>(nameof(HighlightLayer));
    /// <summary>The named cell whose placements are outlined in the complete layout.</summary>
    public static readonly StyledProperty<string?> HighlightCellNameProperty =
        AvaloniaProperty.Register<GdsReviewPreview, string?>(nameof(HighlightCellName));
    /// <summary>The editable ports, in cell-local application coordinates.</summary>
    public static readonly StyledProperty<IReadOnlyList<DetectedPin>?> PortsProperty =
        AvaloniaProperty.Register<GdsReviewPreview, IReadOnlyList<DetectedPin>?>(nameof(Ports));
    /// <summary>The selected port index.</summary>
    public static readonly StyledProperty<int> SelectedPortIndexProperty =
        AvaloniaProperty.Register<GdsReviewPreview, int>(nameof(SelectedPortIndex), -1);
    /// <summary>Whether ports are shown and can be selected.</summary>
    public static readonly StyledProperty<bool> ShowPortsProperty =
        AvaloniaProperty.Register<GdsReviewPreview, bool>(nameof(ShowPorts));
    /// <summary>Whether visible ports can be dragged. Selection remains available while locked.</summary>
    public static readonly StyledProperty<bool> AllowPortEditingProperty =
        AvaloniaProperty.Register<GdsReviewPreview, bool>(nameof(AllowPortEditing));
    /// <summary>Hide and exclude unselected layers from picking while a layer is selected.</summary>
    public static readonly StyledProperty<bool> IsolateSelectedLayerProperty =
        AvaloniaProperty.Register<GdsReviewPreview, bool>(nameof(IsolateSelectedLayer));
    /// <summary>0 automatic detail, 1 fast overview, 2 exact detail.</summary>
    public static readonly StyledProperty<int> DetailLevelProperty =
        AvaloniaProperty.Register<GdsReviewPreview, int>(nameof(DetailLevel));
    public bool IsolateSelectedLayer { get => GetValue(IsolateSelectedLayerProperty); set => SetValue(IsolateSelectedLayerProperty, value); }
    public int DetailLevel { get => GetValue(DetailLevelProperty); set => SetValue(DetailLevelProperty, value); }
    /// <summary>Whether the display omits geometry to stay responsive.</summary>
    public static readonly DirectProperty<GdsReviewPreview, bool> IsPreviewLimitedProperty =
        AvaloniaProperty.RegisterDirect<GdsReviewPreview, bool>(nameof(IsPreviewLimited), c => c.IsPreviewLimited);
    private bool _isPreviewLimited;

    /// <summary>Gets or sets the source scene.</summary>
    public GdsLayerGeometryScene? Scene { get => GetValue(SceneProperty); set => SetValue(SceneProperty, value); }
    /// <summary>Gets or sets the highlighted layer.</summary>
    public GdsLayerPair? HighlightLayer { get => GetValue(HighlightLayerProperty); set => SetValue(HighlightLayerProperty, value); }
    /// <summary>Gets or sets the named cell to outline.</summary>
    public string? HighlightCellName { get => GetValue(HighlightCellNameProperty); set => SetValue(HighlightCellNameProperty, value); }
    /// <summary>Gets or sets the port snapshot; replace it after edits.</summary>
    public IReadOnlyList<DetectedPin>? Ports { get => GetValue(PortsProperty); set => SetValue(PortsProperty, value); }
    /// <summary>Gets or sets the selected port index.</summary>
    public int SelectedPortIndex { get => GetValue(SelectedPortIndexProperty); set => SetValue(SelectedPortIndexProperty, value); }
    /// <summary>Gets or sets port visibility and selection.</summary>
    public bool ShowPorts { get => GetValue(ShowPortsProperty); set => SetValue(ShowPortsProperty, value); }
    /// <summary>Gets or sets whether dragging ports is explicitly enabled.</summary>
    public bool AllowPortEditing { get => GetValue(AllowPortEditingProperty); set => SetValue(AllowPortEditingProperty, value); }
    /// <summary>Gets whether this preview is limited; source geometry remains intact.</summary>
    public bool IsPreviewLimited => _isPreviewLimited;
    /// <summary>Raised when visible source geometry is picked.</summary>
    public event Action<GdsLayerPair>? LayerPicked;
    /// <summary>Raised with the source cell owning the picked geometry.</summary>
    public event Action<string>? CellPicked;
    /// <summary>Raised with the actual root-to-leaf ancestry of the picked placement.</summary>
    public event Action<IReadOnlyList<string>>? CellPathPicked;
    /// <summary>Raised when a port is selected.</summary>
    public event Action<int>? PortPicked;
    /// <summary>Raised during dragging with bounded local X/Y in micrometers.</summary>
    public event Action<int, double, double>? PortMoved;
    public event Action? PortDragStarted;
    public event Action? PortDragCompleted;

    public event Action<IReadOnlyList<GdsReviewCellPlacement>, bool>? PlacementPicked;
    public static readonly StyledProperty<IReadOnlyList<GdsBoundingBox>?> SelectedOccurrenceBoundsProperty =
        AvaloniaProperty.Register<GdsReviewPreview, IReadOnlyList<GdsBoundingBox>?>(nameof(SelectedOccurrenceBounds));
    public IReadOnlyList<GdsBoundingBox>? SelectedOccurrenceBounds
    {
        get => GetValue(SelectedOccurrenceBoundsProperty);
        set => SetValue(SelectedOccurrenceBoundsProperty, value);
    }
    static GdsReviewPreview() =>
        AffectsRender<GdsReviewPreview>(SceneProperty, HighlightLayerProperty, HighlightCellNameProperty, PortsProperty, SelectedPortIndexProperty, ShowPortsProperty, IsolateSelectedLayerProperty, DetailLevelProperty, SelectedOccurrenceBoundsProperty);

    /// <summary>Resets zoom and pan to fit the entire scene.</summary>
    public void Fit()
    {
        _focusBounds = null;
        _zoom = 1;
        _pan = default;
        InvalidateVisual();
    }

    /// <summary>Frames all occurrences of the selected source layer without animation.</summary>
    public void FocusHighlightedLayer()
    {
        if (Scene is not { } scene || HighlightLayer is not { } pair) return;
        GdsBoundingBox? bounds = scene.ReviewSource?.GetLayerBounds(scene.CellName, pair);
        if (scene.ReviewSource is null)
            foreach (var layer in scene.Layers.Where(l => l.Pair == pair))
                foreach (var point in layer.Polygons.SelectMany(p => p.Points).Concat(layer.Texts.Select(t => t.Position)))
                    bounds = bounds is { } box ? box.Include(point) : new(point.X, point.Y, point.X, point.Y);
        if (bounds is null) return;
        CancelGesture();
        _focusBounds = bounds;
        _zoom = 1;
        _pan = default;
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SceneProperty)
        {
            CancelGesture();
            _hierarchy?.Dispose();
            _hierarchy = Scene is { ReviewSource: not null } full ? new GdsReviewHierarchyCache(full) : null;
            _cache = _hierarchy is null && Scene is { } scene ? new GdsReviewGeometryCache(scene) : null;
            SetAndRaise(IsPreviewLimitedProperty, ref _isPreviewLimited, _cache?.IsLimited == true);
            Fit();
            if (IsolateSelectedLayer) FocusHighlightedLayer();
        }
        if (change.Property == HighlightLayerProperty)
        {
            if (HighlightLayer.HasValue && IsolateSelectedLayer) FocusHighlightedLayer();
            else if (!HighlightLayer.HasValue) Fit();
        }
        if (change.Property == IsolateSelectedLayerProperty)
        {
            if (IsolateSelectedLayer) FocusHighlightedLayer();
            else Fit();
        }
        if (change.Property == ShowPortsProperty && !ShowPorts) CancelGesture();
        if (change.Property == AllowPortEditingProperty && !AllowPortEditing) CancelGesture();
    }

    private (double Scale, Point Offset) Transform()
    {
        var sceneBox = Scene!.Bounds;
        var box = _focusBounds ?? sceneBox;
        var width = Math.Max(1, Bounds.Width - 2 * PaddingPixels);
        var height = Math.Max(1, Bounds.Height - 2 * PaddingPixels);
        var minimumExtent = _focusBounds.HasValue ? Math.Max(1e-9, Math.Max(box.Width, box.Height) * .02) : 1;
        if (box.Width == 0 && box.Height == 0) minimumExtent = 1;
        var scale = Math.Min(width / Math.Max(minimumExtent, box.Width), height / Math.Max(minimumExtent, box.Height)) * _zoom;
        return (scale, new Point((Bounds.Width - box.Width * scale) / 2 + (sceneBox.MinX - box.MinX) * scale + _pan.X,
            (Bounds.Height - box.Height * scale) / 2 + (box.MaxY - sceneBox.MaxY) * scale + _pan.Y));
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(BackgroundBrush, new Rect(Bounds.Size));
        if (Scene is null || (_cache is null && _hierarchy is null)) return;
        var (scale, offset) = Transform();
        using var clip = context.PushClip(new Rect(Bounds.Size));
        if (_hierarchy is not null)
            _hierarchy.Render(context,
                new Matrix(scale, 0, 0, -scale, offset.X - Scene.Bounds.MinX * scale, offset.Y + Scene.Bounds.MaxY * scale),
                new Rect(Bounds.Size), HighlightLayer, HighlightCellName, IsolateSelectedLayer, DetailLevel);
        else using (context.PushTransform(new Matrix(scale, 0, 0, scale, offset.X, offset.Y)))
        {
            foreach (var layer in _cache!.Layers)
            {
                if (IsolateSelectedLayer && HighlightLayer.HasValue && HighlightLayer != layer.Pair) continue;
                var selected = HighlightLayer == layer.Pair;
                using var opacity = context.PushOpacity(selected ? 1 : HighlightLayer.HasValue ? GdsReviewSelectionStyle.ContextOpacity : 0.68);
                var fill = selected ? GdsReviewSelectionStyle.Selected : HighlightLayer.HasValue ? GdsReviewSelectionStyle.Context : layer.Fill;
                var pen = selected ? new Pen(Brushes.White, 1 / scale) : null;
                foreach (var geometry in layer.Geometries)
                {
                    context.DrawGeometry(fill, pen, geometry);
                }
                foreach (var text in layer.Texts)
                    context.DrawRectangle(null, new Pen(fill, 1 / scale),
                        new Rect(text.X - 3 / scale, text.Y - 3 / scale, 6 / scale, 6 / scale));
            }
        }
        if (SelectedOccurrenceBounds is { } selectedBounds)
            foreach (var box in selectedBounds)
                context.DrawRectangle(null, new Pen(GdsReviewSelectionStyle.Selected, 3),
                    new Rect(offset.X + (box.MinX - Scene.Bounds.MinX) * scale,
                        offset.Y + (Scene.Bounds.MaxY - box.MaxY) * scale,
                        Math.Max(2, box.Width * scale), Math.Max(2, box.Height * scale)));
        if (!ShowPorts || Ports is null) return;
        for (var i = 0; i < Ports.Count; i++)
        {
            var port = Ports[i];
            var center = new Point(offset.X + port.XUm * scale, offset.Y + port.YUm * scale);
            var angle = port.AngleDegrees * Math.PI / 180;
            var end = center + new Vector(Math.Cos(angle) * 18, Math.Sin(angle) * 18);
            context.DrawLine(new Pen(PortBrush, 2), center, end);
            context.DrawEllipse(i == SelectedPortIndex ? Brushes.White : PortBrush,
                new Pen(BackgroundBrush, 2), center, PortRadius, PortRadius);
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Scene is null) return;
        var point = e.GetPosition(this);
        var buttons = e.GetCurrentPoint(this).Properties;
        if (buttons.IsRightButtonPressed && HighlightLayer.HasValue)
        {
            SetCurrentValue(IsolateSelectedLayerProperty, !IsolateSelectedLayer);
            e.Handled = true;
            return;
        }
        if (buttons.IsMiddleButtonPressed) _panStart = point;
        else if (buttons.IsLeftButtonPressed)
        {
            var (scale, offset) = Transform();
            var local = (point - offset) / scale;
            bool portPicked = false;
            if (ShowPorts && Ports is { } ports)
                for (var i = ports.Count - 1; i >= 0; i--)
                    if ((new Vector(ports[i].XUm, ports[i].YUm) - local).Length * scale <= PickRadius)
                    {
                        portPicked = true;
                        PortPicked?.Invoke(i);
                        if (AllowPortEditing) { _dragPort = i; PortDragStarted?.Invoke(); }
                        break;
                    }
            if (!portPicked)
            {
                double x = local.X + Scene.Bounds.MinX, y = Scene.Bounds.MaxY - local.Y;
                GdsLayerPair pair = default;
                string? pickedCell = null;
                IReadOnlyList<string> pickedPath = [];
                bool picked = _hierarchy is not null
                    ? _hierarchy.TryPick(x, y, PickRadius / scale, out pair, out pickedCell, out pickedPath, IsolateSelectedLayer ? HighlightLayer : null,
                        placements => PlacementPicked?.Invoke(placements, (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != 0))
                    : _cache is { } cache && GdsLayerHitTester.TryPick(IsolateSelectedLayer && HighlightLayer is { } selectedLayer
                        ? cache.PickScene with { Layers = cache.PickScene.Layers.Where(l => l.Pair == selectedLayer).ToArray() }
                        : cache.PickScene, x, y, PickRadius / scale, out pair);
                if (picked)
                {
                    LayerPicked?.Invoke(pair);
                    if (pickedCell is not null) CellPicked?.Invoke(pickedCell);
                    if (pickedPath.Count > 0) CellPathPicked?.Invoke(pickedPath);
                }
            }
        }
        else return;
        if (_panStart.HasValue || _dragPort >= 0)
        { _capturedPointer = e.Pointer; e.Pointer.Capture(this); }
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (_panStart is { } start)
        { _pan += position - start; _panStart = position; InvalidateVisual(); }
        else if (_dragPort >= 0 && ShowPorts && Scene is { } scene && _dragPort < (Ports?.Count ?? 0))
        {
            var (scale, offset) = Transform();
            var local = (position - offset) / scale;
            PortMoved?.Invoke(_dragPort, Math.Clamp(local.X, 0, Math.Max(0, scene.Bounds.Width)),
                Math.Clamp(local.Y, 0, Math.Max(0, scene.Bounds.Height)));
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { base.OnPointerReleased(e); CancelGesture(); }
    /// <inheritdoc/>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    { base.OnPointerCaptureLost(e); CancelGesture(); }
    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { _hierarchy?.Dispose(); CancelGesture(); base.OnDetachedFromVisualTree(e); }


    private void CancelGesture()
    {
        var pointer = _capturedPointer;
        var wasDragging = _dragPort >= 0;
        _capturedPointer = null;
        _dragPort = -1;
        _panStart = null;
        pointer?.Capture(null);
        if (wasDragging) PortDragCompleted?.Invoke();
    }

    /// <inheritdoc/>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Scene is null) return;
        var point = e.GetPosition(this);
        var before = Transform();
        var local = (point - before.Offset) / before.Scale;
        _zoom = Math.Clamp(_zoom * Math.Pow(1.2, e.Delta.Y), 0.1, 1e12);
        var after = Transform();
        _pan += point - (after.Offset + local * after.Scale);
        InvalidateVisual();
        e.Handled = true;
    }
}
