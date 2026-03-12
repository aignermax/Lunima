using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Analysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.Commands;

namespace CAP.Avalonia.ViewModels.Analysis;

/// <summary>
/// ViewModel for the layout compression feature.
/// Automatically rearranges components to minimize chip area while maintaining connectivity.
/// </summary>
public partial class CompressLayoutViewModel : ObservableObject
{
    private const int UpdateFrequency = 5; // Update UI every N iterations

    [ObservableProperty]
    private bool _isCompressing;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _resultText = "";

    [ObservableProperty]
    private int _currentIteration;

    private DesignCanvasViewModel? _canvas;
    private CommandManager? _commandManager;
    private readonly LayoutCompressor _compressor = new();

    /// <summary>
    /// Configures the compressor for the given canvas and command manager.
    /// </summary>
    public void Configure(DesignCanvasViewModel canvas, CommandManager? commandManager = null)
    {
        _canvas = canvas;
        _commandManager = commandManager;
        StatusText = "";
        ResultText = "";
        CurrentIteration = 0;
    }

    /// <summary>
    /// Runs the layout compression algorithm with animated updates.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCompressLayout))]
    private async Task CompressLayout()
    {
        if (_canvas == null || _canvas.Components.Count == 0)
        {
            StatusText = "No components to compress";
            return;
        }

        if (IsCompressing) return;
        IsCompressing = true;
        CurrentIteration = 0;
        StatusText = "Compressing layout...";
        ResultText = "";

        try
        {
            // Calculate original bounding box
            var components = _canvas.Components.Select(c => c.Component).ToList();
            var originalBounds = _compressor.CalculateBoundingBox(components);

            // Store original positions for undo
            var originalPositions = components
                .ToDictionary(c => c, c => (c.PhysicalX, c.PhysicalY));

            // Run compression with progress callback for animation
            await Task.Run(() =>
            {
                var connections = _canvas.Connections
                    .Select(c => c.Connection)
                    .ToList();

                _compressor.CompressLayout(components, connections, iteration =>
                {
                    // Update UI on the UI thread every few iterations
                    Dispatcher.UIThread.Post(() =>
                    {
                        CurrentIteration = iteration;
                        StatusText = $"Compressing... iteration {iteration}/100";

                        // Sync Component.PhysicalX/Y → ComponentViewModel.X/Y
                        foreach (var compVm in _canvas.Components)
                        {
                            compVm.X = compVm.Component.PhysicalX;
                            compVm.Y = compVm.Component.PhysicalY;
                        }
                    });
                });
            });

            // Final sync (in case convergence happened early)
            foreach (var compVm in _canvas.Components)
            {
                compVm.X = compVm.Component.PhysicalX;
                compVm.Y = compVm.Component.PhysicalY;
            }

            // Store final positions for undo command
            var newPositions = components
                .ToDictionary(c => c, c => (c.PhysicalX, c.PhysicalY));

            // Recalculate waveguide routes
            await _canvas.RecalculateRoutesAsync();

            // Calculate new bounding box
            var newBounds = _compressor.CalculateBoundingBox(components);

            // Format results
            double areaReduction = ((originalBounds.area - newBounds.area) / originalBounds.area) * 100;

            ResultText = $"Layout compressed successfully:\n" +
                $"Original: {originalBounds.width:F0} × {originalBounds.height:F0} µm " +
                $"({originalBounds.area / 1_000_000:F2} mm²)\n" +
                $"New: {newBounds.width:F0} × {newBounds.height:F0} µm " +
                $"({newBounds.area / 1_000_000:F2} mm²)\n" +
                $"Area reduction: {areaReduction:F1}%";

            StatusText = $"Compression complete: {areaReduction:F1}% area reduction";

            // Create undo command and execute it (stores in undo history)
            if (_commandManager != null)
            {
                var command = new CompressLayoutCommand(_canvas, originalPositions, newPositions);
                _commandManager.ExecuteCommand(command);
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Compression failed: {ex.Message}";
            ResultText = "";
        }
        finally
        {
            IsCompressing = false;
            CurrentIteration = 0;
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

    /// <summary>
    /// Called when IsCompressing property changes.
    /// </summary>
    partial void OnIsCompressingChanged(bool value)
    {
        CompressLayoutCommand.NotifyCanExecuteChanged();
    }
}
