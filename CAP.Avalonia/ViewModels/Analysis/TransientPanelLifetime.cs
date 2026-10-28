using CAP.Avalonia.Services.Lifetime;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Documents;

namespace CAP.Avalonia.ViewModels.Analysis;

/// <summary>Owns transient-panel configuration, operation admission and retirement.</summary>
internal sealed class TransientPanelLifetime
{
    private readonly ShellWorkScope _work = new();
    private DocumentIdentity? _identity;
    private long _version;
    private AnalysisSweepOperation? _active;
    internal DesignCanvasViewModel? Canvas { get; private set; }
    internal bool IsStopping => _work.IsStopping;
    internal long Version => _version;

    internal void RegisterWith(ApplicationWorkCoordinator? application, Func<Task> stop)
        => _work.RegisterWith(application, stop);

    internal long Configure(DesignCanvasViewModel? canvas)
    {
        if (IsStopping) return _version;
        ++_version;
        _active?.Cancel();
        Canvas = canvas;
        _identity = canvas?.Document.CaptureIdentity();
        return _version;
    }

    internal bool IsConfiguration(long version) => !IsStopping && version == _version;
    internal Task RunAsync(Func<CancellationToken, Task> run) => _work.RunAsync(run);

    internal AnalysisSweepOperation Begin(CancellationToken stop)
    {
        var canvas = Canvas ?? throw new OperationCanceledException("The analysis canvas was cleared.");
        var version = _version;
        return _active = new AnalysisSweepOperation(_work, canvas.Document,
            () => version == _version && ReferenceEquals(Canvas, canvas), stop, stop, _identity);
    }

    internal async ValueTask EndAsync(AnalysisSweepOperation? operation)
    {
        if (ReferenceEquals(_active, operation)) _active = null;
        if (operation != null) await operation.DisposeAsync();
    }

    internal async Task StopAsync()
    {
        try { await _work.StopAsync(); }
        finally { Canvas = null; _identity = null; }
    }
}
