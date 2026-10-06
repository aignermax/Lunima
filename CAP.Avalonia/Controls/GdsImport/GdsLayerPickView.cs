using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CAP.Avalonia.ViewModels.GdsImport.LayerPicker;
using CAP_DataAccess.Import.Gds.LayerPicker;

namespace CAP.Avalonia.Controls.GdsImport;

/// <summary>
/// Interactive preview for the click-to-assign layer picker: renders the
/// flattened top-cell geometry color-coded per (layer, datatype) pair and
/// forwards clicks as scene-coordinate picks to the
/// <see cref="GdsLayerPickerViewModel"/>. The selected layer draws opaque with
/// an outline while the others dim, so the user always sees what a click chose.
/// </summary>
public sealed class GdsLayerPickView : Control
{
    /// <summary>Padding between the scene and the control edge, in pixels.</summary>
    private const double MarginPx = 10;

    /// <summary>Radius of the text-anchor markers, in pixels.</summary>
    private const double TextMarkerRadiusPx = 3.5;

    /// <summary>Click tolerance around text anchors, in pixels.</summary>
    private const double TextPickTolerancePx = 8;

    /// <summary>Upper bound of drawn polygons — a picker preview, not a full GDS viewer.</summary>
    private const int MaxRenderedPolygons = 20000;

    private const byte SelectedFillAlpha = 216;
    private const byte NormalFillAlpha = 140;
    private const byte DimmedFillAlpha = 45;

    /// <summary>The picker ViewModel providing scene, colors and selection.</summary>
    public static readonly StyledProperty<GdsLayerPickerViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<GdsLayerPickView, GdsLayerPickerViewModel?>(nameof(ViewModel));

    /// <summary>Gets or sets the picker ViewModel.</summary>
    public GdsLayerPickerViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    static GdsLayerPickView()
    {
        AffectsRender<GdsLayerPickView>(ViewModelProperty);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ViewModelProperty)
            return;
        if (change.OldValue is GdsLayerPickerViewModel oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        if (change.NewValue is GdsLayerPickerViewModel newVm)
            newVm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GdsLayerPickerViewModel.SelectedRow))
            InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (ViewModel is not { } vm || !TryGetTransform(vm, out var transform))
            return;
        var position = e.GetPosition(this);
        var (xUm, yUm) = transform.ToScene(position);
        vm.PickAt(xUm, yUm, TextPickTolerancePx / transform.Scale);
        e.Handled = true;
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x16)), new Rect(Bounds.Size));
        if (ViewModel is not { } vm || !TryGetTransform(vm, out var transform))
            return;

        var hasSelection = vm.SelectedRow is not null;
        var remainingPolygons = MaxRenderedPolygons;
        foreach (var row in vm.Rows)
        {
            var isSelected = row.IsSelected;
            var alpha = isSelected ? SelectedFillAlpha : hasSelection ? DimmedFillAlpha : NormalFillAlpha;
            var fill = new SolidColorBrush(row.Color, alpha / 255.0);
            var stroke = isSelected ? new Pen(Brushes.White, 1.2) : null;

            foreach (var polygon in row.Geometry.Polygons)
            {
                if (remainingPolygons-- <= 0)
                    break;
                context.DrawGeometry(fill, stroke, BuildGeometry(polygon, transform));
            }
            DrawTextMarkers(context, row, transform, fill, stroke);
        }
    }

    private static void DrawTextMarkers(
        DrawingContext context, GdsLayerPickerLayerRow row, SceneTransform transform,
        IBrush fill, IPen? stroke)
    {
        foreach (var text in row.Geometry.Texts)
        {
            var center = transform.ToView(text.Position.X, text.Position.Y);
            context.DrawEllipse(fill, stroke ?? new Pen(new SolidColorBrush(row.Color), 1),
                center, TextMarkerRadiusPx, TextMarkerRadiusPx);
        }
    }

    private static StreamGeometry BuildGeometry(
        CAP_DataAccess.Import.Gds.GdsPolygon polygon, SceneTransform transform)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        for (var i = 0; i < polygon.Points.Count; i++)
        {
            var point = transform.ToView(polygon.Points[i].X, polygon.Points[i].Y);
            if (i == 0)
                ctx.BeginFigure(point, isFilled: true);
            else
                ctx.LineTo(point);
        }
        ctx.EndFigure(isClosed: true);
        return geometry;
    }

    /// <summary>Fit-to-bounds transform between scene micrometers (Y-up) and view pixels (Y-down).</summary>
    private bool TryGetTransform(GdsLayerPickerViewModel vm, out SceneTransform transform)
    {
        transform = default;
        var bounds = vm.Scene.Bounds;
        var width = Bounds.Width - 2 * MarginPx;
        var height = Bounds.Height - 2 * MarginPx;
        if (width <= 0 || height <= 0)
            return false;
        // A degenerate box (single label, geometry on one line) still gets a
        // finite scale so the markers render and stay clickable.
        var sceneWidth = Math.Max(bounds.Width, 1.0);
        var sceneHeight = Math.Max(bounds.Height, 1.0);
        var scale = Math.Min(width / sceneWidth, height / sceneHeight);
        var offsetX = MarginPx + (width - bounds.Width * scale) / 2;
        var offsetY = MarginPx + (height - bounds.Height * scale) / 2;
        transform = new SceneTransform(bounds.MinX, bounds.MaxY, scale, offsetX, offsetY);
        return true;
    }

    /// <summary>Maps between GDS scene coordinates (µm, Y-up) and control pixels (Y-down).</summary>
    private readonly record struct SceneTransform(
        double MinX, double MaxY, double Scale, double OffsetX, double OffsetY)
    {
        /// <summary>Scene µm → view pixels.</summary>
        public Point ToView(double xUm, double yUm) =>
            new(OffsetX + (xUm - MinX) * Scale, OffsetY + (MaxY - yUm) * Scale);

        /// <summary>View pixels → scene µm.</summary>
        public (double XUm, double YUm) ToScene(Point p) =>
            (MinX + (p.X - OffsetX) / Scale, MaxY - (p.Y - OffsetY) / Scale);
    }
}
