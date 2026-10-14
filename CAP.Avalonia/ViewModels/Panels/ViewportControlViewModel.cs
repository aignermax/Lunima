using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// ViewModel for viewport control (zoom, pan, navigation).
/// Handles all canvas viewport manipulation and navigation.
/// Max 250 lines per CLAUDE.md guideline.
/// </summary>
public partial class ViewportControlViewModel : ObservableObject
{
    private readonly DesignCanvasViewModel _canvas;

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    /// <summary>
    /// Callback to update status text in the UI.
    /// </summary>
    public Action<string>? UpdateStatus { get; set; }

    /// <summary>
    /// Callback to get the current canvas viewport size (width, height) in screen pixels.
    /// Set by the View code-behind (MainWindow) after initialization.
    /// </summary>
    public Func<(double width, double height)>? GetViewportSize { get; set; }

    /// <summary>
    /// True when zoom-to-fit frames every die (System workspace); otherwise it frames the
    /// content of the current die. Wired by <c>MainViewModel</c>.
    /// </summary>
    public Func<bool>? FitsWholeAssembly { get; set; }

    /// <summary>Canvas position of the pointer, or null when it is not over the canvas (wired by the view).</summary>
    public Func<(double X, double Y)?>? GetCursorCanvasPosition { get; set; }

    /// <summary>Extent (µm) zoom-to-fit frames around content without area (a single point).</summary>
    private const double MinFitExtentMicrometers = 200.0;

    public ViewportControlViewModel(DesignCanvasViewModel canvas)
    {
        _canvas = canvas;
    }

    [RelayCommand]
    private void ZoomIn()
    {
        ZoomLevel = Math.Min(ZoomLevel * 1.2, CanvasZoomLimits.Max);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        ZoomLevel = Math.Max(ZoomLevel / 1.2, CanvasZoomLimits.Min);
    }

    [RelayCommand]
    private void ResetZoom()
    {
        ZoomLevel = 1.0;
    }

    [RelayCommand]
    private void ResetPan()
    {
        _canvas.PanX = 0;
        _canvas.PanY = 0;
    }

    /// <summary>
    /// Pans the canvas so the given coordinate (in micrometers) is centered in view.
    /// Preserves the current zoom level.
    /// </summary>
    /// <param name="centerX">X coordinate in micrometers to center on.</param>
    /// <param name="centerY">Y coordinate in micrometers to center on.</param>
    public void NavigateCanvasTo(double centerX, double centerY)
    {
        var (vpWidth, vpHeight) = GetViewportSize?.Invoke() ?? (900, 800);

        _canvas.PanX = vpWidth / 2 - centerX * ZoomLevel;
        _canvas.PanY = vpHeight / 2 - centerY * ZoomLevel;
    }

    /// <summary>
    /// Adjusts zoom and pan to fit all components in the viewport.
    /// Applies 10% padding around the design. Does nothing on empty canvas.
    /// </summary>
    /// <param name="viewportWidth">Viewport width in screen pixels.</param>
    /// <param name="viewportHeight">Viewport height in screen pixels.</param>
    public void ZoomToFit(double viewportWidth, double viewportHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        var bounds = Canvas.DieSurface.ZoomToFitTarget.Resolve(
            _canvas, FitsWholeAssembly?.Invoke() ?? false, GetCursorCanvasPosition?.Invoke());
        if (bounds == null)
        {
            UpdateStatus?.Invoke("No components to fit");
            return;
        }

        var padded = BoundingBoxCalculator.WithPadding(
            bounds.Value.IsEmpty ? AtLeast(bounds.Value, MinFitExtentMicrometers) : bounds.Value,
            BoundingBoxCalculator.DefaultPaddingFraction);

        if (padded.IsEmpty) return;

        var (zoom, panX, panY) = BoundingBoxCalculator.CalculateZoomToFit(
            padded, viewportWidth, viewportHeight);

        ZoomLevel = zoom;
        _canvas.PanX = panX;
        _canvas.PanY = panY;
        // No percentage here: the status bar shows the live zoom, and a number in the message
        // would go stale with the next zoom step.
        UpdateStatus?.Invoke(Services.Localization.LocalizationService.Instance.Translate("Status.ZoomedToFit"));
    }

    private static BoundingBox AtLeast(BoundingBox box, double extent)
    {
        double halfWidth = Math.Max(box.Width, extent) / 2, halfHeight = Math.Max(box.Height, extent) / 2;
        return new BoundingBox(box.CenterX - halfWidth, box.CenterY - halfHeight, box.CenterX + halfWidth, box.CenterY + halfHeight);
    }

    /// <summary>
    /// Fits the rectangle (µm) into the current viewport with the usual 10% padding — e.g. a
    /// die on the assembly. Does nothing for an empty rectangle.
    /// </summary>
    public void ZoomToRect(double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        var (vpWidth, vpHeight) = GetViewportSize?.Invoke() ?? (900, 800);
        var padded = BoundingBoxCalculator.WithPadding(
            new BoundingBox(x, y, x + width, y + height), BoundingBoxCalculator.DefaultPaddingFraction);
        var (zoom, panX, panY) = BoundingBoxCalculator.CalculateZoomToFit(padded, vpWidth, vpHeight);
        ZoomLevel = zoom;
        _canvas.PanX = panX;
        _canvas.PanY = panY;
    }
}
