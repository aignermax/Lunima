namespace CAP.Avalonia.ViewModels.Canvas.Services;

public partial class RoutingOrchestrator
{
    private int _suspendRouting;

    /// <summary>Invalidates queued canvas topology callbacks from older routing results.</summary>
    public int MutationGeneration { get; private set; }

    /// <summary>
    /// Cancel and drain routing without blocking the UI. The worker mutates shared routes,
    /// so history must wait for the semaphore, not merely request cancellation.
    /// </summary>
    public async Task<bool> ExecuteHistoryChangeAsync(Func<bool> change, Func<bool> canChange)
    {
        if (!canChange()) return false;
        _routingCts?.Cancel();
        await _routingSemaphore.WaitAsync();
        try
        {
            if (!canChange()) return false;
            MutationGeneration++;
            Interlocked.Increment(ref _gridRequest);
            return change();
        }
        finally { _routingSemaphore.Release(); }
    }

    /// <summary>Lets callers distinguish an exact move restore from edits requiring rerouting.</summary>
    public int RestoredGeometryVersion { get; private set; }

    /// <summary>Undo a move without searching from intermediate component positions.</summary>
    public void RestoreMove(Action moveBack, Action restoreGeometry)
    {
        _suspendRouting++;
        MutationGeneration++;
        try
        {
            moveBack();
            restoreGeometry();
            RestoredGeometryVersion++;
            RepaintRequested?.Invoke();
        }
        finally { _suspendRouting--; }
    }
}
