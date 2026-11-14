namespace CAP_Core.Analysis.LogicAnalysis;

/// <summary>
/// Hands the thread that owns the live design back to its message loop between slices of a
/// capture, so queued input and rendering run before the next slice reads more live state. The
/// capture resumes on the owner thread. The design may be edited while the owner runs other work,
/// so an implementation throws <see cref="OperationCanceledException"/> on resumption once the
/// state captured so far can no longer be trusted.
/// </summary>
public interface ICaptureYield
{
    /// <summary>Lets the owner run queued work once, then resumes the capture on the owner thread.</summary>
    /// <param name="cancellationToken">Cancels the capture while it is yielded.</param>
    /// <exception cref="OperationCanceledException">The capture became obsolete while yielded.</exception>
    Task YieldAsync(CancellationToken cancellationToken);
}
