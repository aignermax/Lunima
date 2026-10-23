using System.Diagnostics;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Edit;

/// <summary>
/// Awaits routing passes for the edit tools without blocking the UI thread: the pass itself
/// runs on a background thread, and the poll for "no pass running" uses <c>Task.Delay</c>.
/// A newer pass supersedes (cancels) an older one, so "our" pass returning is not enough —
/// the tools wait until the canvas is idle.
/// </summary>
public static class RoutingAwaiter
{
    /// <summary>Longest an edit tool waits for routing before reporting it as still running.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>Starts a full routing pass and waits until routing is idle.</summary>
    /// <returns>True when routing finished within <paramref name="timeout"/>.</returns>
    public static async Task<bool> RerouteAndWaitAsync(DesignCanvasViewModel canvas, TimeSpan? timeout = null)
    {
        await canvas.RecalculateRoutesAsync();
        return await WaitForIdleAsync(canvas, timeout);
    }

    /// <summary>Waits until no routing pass is running.</summary>
    /// <returns>True when routing is idle within <paramref name="timeout"/>.</returns>
    public static async Task<bool> WaitForIdleAsync(DesignCanvasViewModel canvas, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var limit = timeout ?? DefaultTimeout;
        var clock = Stopwatch.StartNew();
        while (canvas.Routing.IsRouting)
        {
            if (clock.Elapsed > limit)
                return false;
            await Task.Delay(PollInterval, cancellationToken);
        }
        return true;
    }
}
