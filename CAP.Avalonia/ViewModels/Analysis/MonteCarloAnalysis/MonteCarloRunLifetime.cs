using CAP.Avalonia.Services.Lifetime;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Documents;

namespace CAP.Avalonia.ViewModels.Analysis.MonteCarloAnalysis;

/// <summary>Owns one panel's configured document and its admitted fabrication jobs.</summary>
internal sealed class MonteCarloRunLifetime
{
    private readonly ShellWorkScope _work = new();
    private DocumentIdentity? _identity;
    private long _version;
    private AnalysisSweepOperation? _active;
    internal DesignCanvasViewModel? Canvas { get; private set; }
    internal bool IsStopping => _work.IsStopping;

    internal void Register(ApplicationWorkCoordinator? application, Func<Task> retire) => _work.RegisterWith(application, retire);

    internal void Configure(DesignCanvasViewModel canvas)
    {
        if (IsStopping) return;
        _version++;
        _active?.Cancel();
        Canvas = canvas;
        try { _identity = canvas.Document.CaptureIdentity(); }
        catch (OperationCanceledException) { _identity = null; }
    }

    internal Task RunAsync(Func<CancellationToken, Task> work) => _work.RunAsync(work);

    internal AnalysisSweepOperation? Begin(CancellationToken stop)
    {
        var canvas = Canvas;
        if (IsStopping || canvas == null || _identity is not { } identity
            || !canvas.Document.IsCurrentIdentity(identity)) return null;
        var version = _version;
        return _active = new(_work, canvas.Document,
            () => _version == version && ReferenceEquals(Canvas, canvas), stop, stop, identity);
    }

    internal bool Cancel()
    {
        if (_active?.IsContextCurrent != true) return false;
        _active.Cancel();
        return true;
    }

    internal async Task EndAsync(AnalysisSweepOperation? operation)
    {
        if (ReferenceEquals(_active, operation)) _active = null;
        if (operation != null) await operation.DisposeAsync();
    }

    internal Task StopAsync() => _work.StopAsync();
    internal void Release() { Canvas = null; _identity = null; }
}
