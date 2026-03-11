using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Analysis;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Analysis;

/// <summary>
/// ViewModel for the layout compression feature.
/// Automatically rearranges components to minimize chip area while maintaining connectivity.
/// </summary>
public partial class CompressLayoutViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isCompressing;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _resultText = "";

    private DesignCanvasViewModel? _canvas;
    private readonly LayoutCompressor _compressor = new();

    /// <summary>
    /// Configures the compressor for the given canvas.
    /// </summary>
    public void Configure(DesignCanvasViewModel canvas)
    {
        _canvas = canvas;
        StatusText = "";
        ResultText = "";
    }

    /// <summary>
    /// Runs the layout compression algorithm.
    /// </summary>
    [RelayCommand]
    private async Task CompressLayout()
    {
        if (_canvas == null || _canvas.Components.Count == 0)
        {
            StatusText = "No components to compress";
            return;
        }

        if (IsCompressing) return;
        IsCompressing = true;
        StatusText = "Compressing layout...";
        ResultText = "";

        try
        {
            // Calculate original bounding box
            var originalBounds = _compressor.CalculateBoundingBox(
                _canvas.Components.Select(c => c.Component).ToList());

            // Store original positions for undo
            var originalPositions = _canvas.Components
                .ToDictionary(c => c, c => (c.X, c.Y));

            // Run compression
            await Task.Run(() =>
            {
                var components = _canvas.Components
                    .Select(c => c.Component)
                    .ToList();

                var connections = _canvas.Connections
                    .Select(c => c.Connection)
                    .ToList();

                _compressor.CompressLayout(components, connections);
            });

            // Update ViewModel positions
            foreach (var compVm in _canvas.Components)
            {
                compVm.X = compVm.Component.PhysicalX;
                compVm.Y = compVm.Component.PhysicalY;
            }

            // Recalculate waveguide routes
            await _canvas.RecalculateRoutesAsync();

            // Calculate new bounding box
            var newBounds = _compressor.CalculateBoundingBox(
                _canvas.Components.Select(c => c.Component).ToList());

            // Format results
            double areaReduction = ((originalBounds.area - newBounds.area) / originalBounds.area) * 100;

            ResultText = $"Layout compressed successfully:\n" +
                $"Original: {originalBounds.width:F0} × {originalBounds.height:F0} µm " +
                $"({originalBounds.area / 1_000_000:F2} mm²)\n" +
                $"New: {newBounds.width:F0} × {newBounds.height:F0} µm " +
                $"({newBounds.area / 1_000_000:F2} mm²)\n" +
                $"Area reduction: {areaReduction:F1}%";

            StatusText = $"Compression complete: {areaReduction:F1}% area reduction";
        }
        catch (Exception ex)
        {
            StatusText = $"Compression failed: {ex.Message}";
            ResultText = "";
        }
        finally
        {
            IsCompressing = false;
        }
    }

    /// <summary>
    /// Checks whether compression can be executed.
    /// </summary>
    private bool CanCompressLayout()
    {
        return _canvas != null &&
               _canvas.Components.Count > 0 &&
               !IsCompressing;
    }
}
