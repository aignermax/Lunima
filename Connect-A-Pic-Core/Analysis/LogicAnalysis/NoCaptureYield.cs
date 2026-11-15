namespace CAP_Core.Analysis.LogicAnalysis;

/// <summary>Never yields: the capture stays one synchronous slice, for callers without an owner message loop.</summary>
public sealed class NoCaptureYield : ICaptureYield
{
    /// <summary>The shared instance; the type holds no state.</summary>
    public static NoCaptureYield Instance { get; } = new();

    private NoCaptureYield()
    {
    }

    /// <inheritdoc />
    public Task YieldAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
