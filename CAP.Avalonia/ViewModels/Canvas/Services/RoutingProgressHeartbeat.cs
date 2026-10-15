using Avalonia.Threading;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Components.Connections;

namespace CAP.Avalonia.ViewModels.Canvas.Services;

/// <summary>Refreshes a routing pass' elapsed time while its worker is busy on one connection.</summary>
internal sealed class RoutingProgressHeartbeat : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(250);
    private readonly Func<bool> _isCurrent;
    private readonly Func<string> _progressText;
    private readonly Action<string> _publish;
    private readonly DispatcherTimer _timer;
    private bool _complex;
    private RoutingPhase _phase;
    private bool _disposed;
    private string? _lastText;

    /// <summary>Starts UI-thread updates for the current pass until disposed or superseded.</summary>
    public RoutingProgressHeartbeat(Func<bool> isCurrent, Func<string> progressText, Action<string> publish)
    {
        _isCurrent = isCurrent;
        _progressText = progressText;
        _publish = publish;
        _timer = new DispatcherTimer { Interval = RefreshInterval };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>Adds the complex-search phase while retaining counts and elapsed time.</summary>
    public void ComplexRouteStarted()
    {
        _complex = _phase == RoutingPhase.Initial;
        Refresh();
    }

    /// <summary>Clears the complex phase when a connection finishes.</summary>
    public void ConnectionCompleted()
    {
        _complex = false;
        Refresh();
    }

    /// <summary>Switches stages and clears any completed initial-search subphase.</summary>
    public void PhaseChanged(RoutingPhase phase)
    {
        _phase = phase;
        _complex = false;
        Refresh();
    }

    private void OnTick(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (_disposed) return;
        if (!_isCurrent())
        {
            Dispose();
            return;
        }
        var text = _progressText();
        if (_complex)
            text += " · " + LocalizationService.Instance.Translate("Routing.Status.ComplexPath");
        else if (_phase != RoutingPhase.Initial)
            text += " · " + LocalizationService.Instance.Translate("Routing.Status." + _phase);
        if (text == _lastText) return;
        _lastText = text;
        _publish(text);
    }

    /// <summary>Stops the timer and prevents queued callbacks from publishing.</summary>
    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
