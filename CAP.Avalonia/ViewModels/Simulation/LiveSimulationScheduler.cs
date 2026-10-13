namespace CAP.Avalonia.ViewModels.Simulation;

/// <summary>
/// Keeps a switched-on CW simulation current while the design is edited. Every design
/// change calls <see cref="Request"/>; requests are debounced, a newer request cancels a
/// stale run, and runs never overlap. While a routing pass is in flight nothing is
/// simulated (wire geometry is still changing) — <see cref="OnRoutingFinished"/> picks
/// the run up once the routes are final.
/// </summary>
/// <remarks>
/// Call every member from the UI thread: the debounce continuation resumes on the
/// caller's synchronization context, so the simulation delegate runs there too and does
/// its own heavy work off-thread.
/// </remarks>
public sealed class LiveSimulationScheduler
{
    /// <summary>Quiet time after the last change before the simulation re-runs.</summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(150);

    private readonly Func<bool> _isSimulationOn;
    private readonly Func<bool> _isRouting;
    private readonly Func<CancellationToken, Task> _simulate;
    private readonly TimeSpan _debounce;
    private CancellationTokenSource? _pendingCts;
    private Task _latest = Task.CompletedTask;

    /// <summary>Initializes the scheduler.</summary>
    /// <param name="isSimulationOn">Whether the live simulation is switched on; off means requests are ignored.</param>
    /// <param name="isRouting">Whether a routing pass is in flight.</param>
    /// <param name="simulate">Runs one simulation; must honour the token (a cancelled run applies no result).</param>
    /// <param name="debounce">Quiet time before a run; <see cref="DefaultDebounce"/> when null.</param>
    public LiveSimulationScheduler(
        Func<bool> isSimulationOn,
        Func<bool> isRouting,
        Func<CancellationToken, Task> simulate,
        TimeSpan? debounce = null)
    {
        _isSimulationOn = isSimulationOn;
        _isRouting = isRouting;
        _simulate = simulate;
        _debounce = debounce ?? DefaultDebounce;
    }

    /// <summary>True while a run was requested because routing was busy and waits for it to finish.</summary>
    public bool IsWaitingForRouting { get; private set; }

    /// <summary>Number of simulations started (diagnostics and tests).</summary>
    public int RunCount { get; private set; }

    /// <summary>
    /// The design changed: re-simulate after the debounce, cancelling any pending or
    /// running stale simulation. Ignored while the simulation is off.
    /// </summary>
    public void Request()
    {
        if (!_isSimulationOn())
            return;
        CancelPending();
        var cts = new CancellationTokenSource();
        _pendingCts = cts;
        _latest = RunAfterDebounceAsync(_latest, cts.Token);
    }

    /// <summary>A routing pass ended: the routes are final, so the simulation catches up.</summary>
    public void OnRoutingFinished()
    {
        IsWaitingForRouting = false;
        Request();
    }

    /// <summary>Drops any pending or running live simulation (the simulation was switched off).</summary>
    public void Cancel()
    {
        IsWaitingForRouting = false;
        CancelPending();
    }

    /// <summary>
    /// Completes once no live simulation is pending or running — including runs requested
    /// while awaiting. Never throws.
    /// </summary>
    public async Task WhenSettledAsync()
    {
        Task observed;
        do
        {
            observed = _latest;
            await WaitQuietlyAsync(observed);
        }
        while (!ReferenceEquals(observed, _latest));
    }

    private void CancelPending()
    {
        _pendingCts?.Cancel();
        _pendingCts = null;
    }

    private async Task RunAfterDebounceAsync(Task previous, CancellationToken token)
    {
        await DebounceAsync(token);
        // Even a superseded request waits for its predecessor: the next run chains on this
        // task, so finishing early would let two simulations overlap.
        await WaitQuietlyAsync(previous);
        try
        {
            if (token.IsCancellationRequested || !_isSimulationOn())
                return;
            if (_isRouting())
            {
                IsWaitingForRouting = true;
                return;
            }
            RunCount++;
            await _simulate(token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request or switched off — the newer run owns the result.
        }
    }

    private async Task DebounceAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_debounce, token);
        }
        catch (OperationCanceledException)
        {
            // A newer request restarted the quiet time; the caller checks the token.
        }
    }

    /// <summary>
    /// Waits for the previous (already cancelled) run so runs never overlap. Its outcome
    /// was reported by the simulation delegate itself, so a fault is not re-raised here.
    /// </summary>
    private static async Task WaitQuietlyAsync(Task previous)
    {
        try
        {
            await previous;
        }
        catch (Exception)
        {
            // Reported by the run that failed; this run only needs it to be finished.
        }
    }
}
