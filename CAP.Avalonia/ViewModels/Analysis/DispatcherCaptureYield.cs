using Avalonia.Threading;
using CAP_Core.Analysis.LogicAnalysis;

namespace CAP.Avalonia.ViewModels.Analysis;

/// <summary>
/// Cuts a capture of live design state into dispatcher frames. Between slices the UI thread first
/// runs every queued job above <see cref="DispatcherPriority.Background"/> (input, rendering, other
/// work), then the capture resumes on it after re-validating that its operation may still publish.
/// Outside a running Avalonia dispatcher job (plain unit tests, worker threads) there is no frame
/// to give back, so the capture stays one synchronous slice and is only re-validated.
/// </summary>
internal sealed class DispatcherCaptureYield : ICaptureYield
{
    private readonly Action _throwIfObsolete;

    /// <summary>Creates a yield that re-validates its operation after every frame.</summary>
    /// <param name="throwIfObsolete">
    /// Throws <see cref="OperationCanceledException"/> once the operation may no longer publish —
    /// a design edit, a cancel, a panel reconfiguration or a shell stop during the frame.
    /// </param>
    public DispatcherCaptureYield(Action throwIfObsolete) =>
        _throwIfObsolete = throwIfObsolete ?? throw new ArgumentNullException(nameof(throwIfObsolete));

    /// <inheritdoc />
    public async Task YieldAsync(CancellationToken cancellationToken)
    {
        if (SynchronizationContext.Current is AvaloniaSynchronizationContext)
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background, cancellationToken);
        _throwIfObsolete();
    }
}
