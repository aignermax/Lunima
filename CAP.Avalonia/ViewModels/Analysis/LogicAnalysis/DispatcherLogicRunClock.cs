using Avalonia.Threading;

namespace CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;

/// <summary>
/// Production <see cref="ILogicRunClock"/> backed by a <see cref="DispatcherTimer"/>:
/// the ticks arrive on the UI thread, so a tick can refresh the panel's observable
/// collections and the canvas badges directly.
/// </summary>
public sealed class DispatcherLogicRunClock : ILogicRunClock, IDisposable
{
    private readonly DispatcherTimer _timer = new();
    private bool _disposed;

    /// <summary>Initializes the clock; the timer forwards every elapsed interval as <see cref="Tick"/>.</summary>
    public DispatcherLogicRunClock() => _timer.Tick += OnTick;
    private void OnTick(object? sender, EventArgs args) => Tick?.Invoke(this, args);

    /// <inheritdoc/>
    public event EventHandler? Tick;

    /// <inheritdoc/>
    public void Start(TimeSpan interval)
    {
        if (_disposed) return;
        _timer.Interval = interval;
        _timer.Start();
    }

    /// <inheritdoc/>
    public void Stop() => _timer.Stop();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        Tick = null;
    }
}
