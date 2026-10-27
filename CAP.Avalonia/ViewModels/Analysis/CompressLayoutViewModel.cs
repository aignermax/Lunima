using CAP_Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Analysis;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using Avalonia.Threading;

namespace CAP.Avalonia.ViewModels.Analysis;

/// <summary>
/// ViewModel for the layout compression feature.
/// Automatically rearranges components to minimize chip area while maintaining connectivity.
/// </summary>
public partial class CompressLayoutViewModel : ObservableObject, IAsyncDisposable
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompressLayoutCommand))]
    private bool _isCompressing;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _resultText = "";

    [ObservableProperty]
    private int _currentIteration;

    [ObservableProperty]
    private int _maxIterations = 100;

    private readonly ErrorConsoleService? _errorConsole;
    private DesignCanvasViewModel? _canvas;
    private readonly LayoutCompressor _compressor = new();
    private readonly ILayoutCompressionRunner _runner;
    private readonly Services.Lifetime.ShellWorkScope _work = new();
    private AnalysisSweepOperation? _activeRun;
    private CAP_Core.Documents.DocumentIdentity? _configuredDocument;
    private long _version;
    private Task? _retirement;

    public Task StopAsync() => _retirement ??= RetireAsync();
    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task RetireAsync()
    {
        try { await _work.StopAsync(); }
        finally { _canvas = null; _configuredDocument = null; }
    }

    /// <summary>Initializes a new instance of <see cref="CompressLayoutViewModel"/>.</summary>
    /// <param name="errorConsole">Optional service for error logging.</param>
    public CompressLayoutViewModel(ErrorConsoleService? errorConsole = null,
        ILayoutCompressionRunner? runner = null, Services.Lifetime.ApplicationWorkCoordinator? applicationWork = null)
    {
        _errorConsole = errorConsole;
        _runner = runner ?? new LayoutCompressionRunner();
        _work.RegisterWith(applicationWork, StopAsync);
    }

    /// <summary>
    /// Configures the compressor for the given canvas.
    /// </summary>
    public void Configure(DesignCanvasViewModel canvas)
    {
        if (_work.IsStopping) return;
        var version = ++_version;
        _activeRun?.Cancel();
        _canvas = canvas;
        _configuredDocument = canvas.Document.CaptureIdentity();
        StatusText = "";
        if (_work.IsStopping || version != _version) return;
        ResultText = "";
        if (_work.IsStopping || version != _version) return;
        CompressLayoutCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Runs the layout compression algorithm.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCompressLayout))]
    private Task CompressLayout() => _work.RunAsync(CompressLayoutCoreAsync);

    private async Task CompressLayoutCoreAsync(CancellationToken stop)
    {
        if (_canvas == null || _canvas.Components.Count == 0)
        {
            StatusText = LocalizationService.Instance.Translate("Analysis.Compress.NoComponents");
            return;
        }
        if (IsCompressing) return;
        var canvas = _canvas;
        var version = _version;
        var targets = canvas.Components.ToArray();
        var components = targets.Select(c => c.Component).ToList();
        AnalysisSweepOperation? operation = null;
        var acceptProgress = true;
        var geometryCommitted = false;
        var routesSettled = false;
        Action markCommitted = () => geometryCommitted = true;
        try
        {
            operation = new AnalysisSweepOperation(_work, canvas.Document,
                () => version == _version && ReferenceEquals(canvas, _canvas)
                    && targets.All(canvas.Components.Contains), stop, stop, _configuredDocument);
            _activeRun = operation;
            operation.ThrowIfObsolete();
            var snapshot = LayoutCompressionSnapshot.Capture(components,
                canvas.Connections.Select(c => c.Connection).ToArray());
            var originalBounds = _compressor.CalculateBoundingBox(components);
            IsCompressing = true;
            operation.ThrowIfObsolete();
            StatusText = LocalizationService.Instance.Translate("Analysis.Compress.Running");
            operation.ThrowIfObsolete();
            ResultText = "";
            operation.ThrowIfObsolete();
            CurrentIteration = 0;
            operation.ThrowIfObsolete();

            var progress = new Progress<LayoutCompressionFrame>(frame =>
            {
                if (!acceptProgress || !operation.CanPublish) return;
                try
                {
                    ApplyFrame(canvas, targets, frame, operation, markCommitted);
                    operation.ThrowIfObsolete();
                    CurrentIteration = frame.Iteration;
                    operation.ThrowIfObsolete();
                    StatusText = string.Format(LocalizationService.Instance.Translate("Analysis.Compress.RunningIteration"),
                        frame.Iteration, MaxIterations);
                }
                catch (OperationCanceledException) { }
            });
            var result = await _runner.RunAsync(snapshot, operation.Token, progress);
            acceptProgress = false;
            operation.ThrowIfObsolete();
            ApplyFrame(canvas, targets, result, operation, markCommitted);
            operation.ThrowIfObsolete();
            await canvas.Routing.RecalculateRoutesAsync(operation.DocumentLease, operation, operation.Token);
            operation.ThrowIfObsolete();
            routesSettled = true;

            var newBounds = _compressor.CalculateBoundingBox(canvas.Components.Select(c => c.Component).ToList());
            double areaReduction = originalBounds.area == 0 ? 0
                : ((originalBounds.area - newBounds.area) / originalBounds.area) * 100;
            ResultText = $"Layout compressed successfully:\n" +
                $"Original: {originalBounds.width:F0} × {originalBounds.height:F0} µm " +
                $"({originalBounds.area / 1_000_000:F2} mm²)\n" +
                $"New: {newBounds.width:F0} × {newBounds.height:F0} µm " +
                $"({newBounds.area / 1_000_000:F2} mm²)\n" +
                $"Area reduction: {areaReduction:F1}%";
            operation.ThrowIfObsolete();
            StatusText = string.Format(LocalizationService.Instance.Translate("Analysis.Compress.Complete"), areaReduction);
            operation.ThrowIfObsolete();
            CurrentIteration = MaxIterations;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (operation?.CanPublish != true) return;
            _errorConsole?.LogError($"Layout compression failed: {ex.Message}", ex);
            if (!operation.CanPublish) return;
            StatusText = string.Format(LocalizationService.Instance.Translate("Analysis.Compress.Failed"), ex.Message);
            if (operation.CanPublish) ResultText = "";
        }
        finally
        {
            acceptProgress = false;
            if (ReferenceEquals(_activeRun, operation)) _activeRun = null;
            try
            {
                if (geometryCommitted && !routesSettled && operation != null)
                    await operation.CompleteDocumentChangesAsync((lease, token) =>
                        canvas.Routing.RecalculateRoutesAsync(lease, cancellationToken: token));
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (operation?.CanPublish == true)
                    _errorConsole?.LogError($"Layout compression route cleanup failed: {error.Message}", error);
            }
            finally
            {
                try { IsCompressing = false; }
                finally { if (operation != null) await operation.DisposeAsync(); }
            }
        }
    }

    private static void ApplyFrame(DesignCanvasViewModel canvas, ComponentViewModel[] targets,
        LayoutCompressionFrame frame, AnalysisSweepOperation operation, Action markCommitted)
    {
        Dispatcher.UIThread.VerifyAccess();
        using var suspended = canvas.Routing.SuspendAutomaticRouting();
        for (var i = 0; i < targets.Length; i++)
        {
            var target = targets[i];
            var position = frame.Positions[i];
            if (target.Component.PhysicalX != position.X || target.X != position.X)
                operation.CommitAndAdvance(() =>
                {
                    markCommitted();
                    target.Component.PhysicalX = position.X;
                    target.X = position.X;
                });
            if (target.Component.PhysicalY != position.Y || target.Y != position.Y)
                operation.CommitAndAdvance(() =>
                {
                    markCommitted();
                    target.Component.PhysicalY = position.Y;
                    target.Y = position.Y;
                });
        }
    }

    /// <summary>
    /// Checks whether compression can be executed.
    /// </summary>
    private bool CanCompressLayout()
    {
        return !_work.IsStopping && _canvas != null &&
               _canvas.Components.Count > 0 &&
               !IsCompressing;
    }
}
