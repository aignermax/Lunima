using System.Diagnostics;

namespace CAP_Core.Export.Processes;

/// <summary>A redirected tool whose ordinary descendants remain owned until retirement.</summary>
public sealed class OwnedProcess : IAsyncDisposable
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);
    private readonly IProcessLifetime _lifetime;
    private readonly OwnedProcessPipes _pipes;
    private readonly object _gate = new();
    private Task? _retirement;
    private bool _disposed;

    private OwnedProcess(IProcessLifetime lifetime, OwnedProcessPipes pipes)
    {
        _lifetime = lifetime;
        _pipes = pipes;
    }

    /// <summary>Launches a factory-built command with redirected streams and an owned Job/process group.</summary>
    public static OwnedProcess Start(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (startInfo.UseShellExecute || !string.IsNullOrEmpty(startInfo.UserName))
            throw new NotSupportedException("Owned tools require direct execution under the current user.");
        var pipes = new OwnedProcessPipes(startInfo);
        try
        {
            IProcessLifetime lifetime = OperatingSystem.IsWindows()
                ? WindowsProcessLifetime.Start(startInfo, pipes)
                : UnixProcessLifetime.Start(startInfo, pipes);
            pipes.CloseChildCopies();
            return new OwnedProcess(lifetime, pipes);
        }
        catch { pipes.Dispose(); throw; }
    }

    /// <summary>The original tool's process identifier.</summary>
    public int Id => _lifetime.Id;
    /// <summary>Whether the original tool has exited, independently of descendants.</summary>
    public bool HasExited => _lifetime.Exit.IsCompletedSuccessfully;
    /// <summary>The original tool's exit code after <see cref="WaitForExitAsync"/> completes.</summary>
    public int ExitCode => HasExited ? _lifetime.Exit.Result : throw new InvalidOperationException("The tool has not exited.");
    /// <summary>Input stream owned by this invocation.</summary>
    public StreamWriter StandardInput => _pipes.Input;
    /// <summary>Output stream owned by this invocation.</summary>
    public StreamReader StandardOutput => _pipes.Output;
    /// <summary>Error stream owned by this invocation.</summary>
    public StreamReader StandardError => _pipes.Error;

    /// <summary>Observes root exit; cancellation interrupts the wait without abandoning ownership.</summary>
    public Task WaitForExitAsync(CancellationToken cancellationToken = default) =>
        _lifetime.Exit.WaitAsync(cancellationToken);

    /// <summary>Terminates the retained process scope and reaps its root, including after normal root exit.</summary>
    public Task StopAsync()
    {
        lock (_gate)
            return _retirement ??= Task.Run(RetireAsync);
    }

    private async Task RetireAsync()
    {
        using var deadline = new CancellationTokenSource(CleanupTimeout);
        _lifetime.Terminate();
        await _lifetime.Exit.WaitAsync(deadline.Token).ConfigureAwait(false);
        await _lifetime.ReapAsync(deadline.Token).ConfigureAwait(false);
    }

    /// <summary>Retires the scope before closing its streams and native ownership handles.</summary>
    public async ValueTask DisposeAsync()
    {
        try { await StopAsync().ConfigureAwait(false); }
        finally
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    try { _pipes.Dispose(); }
                    finally { _lifetime.Dispose(); }
                }
            }
        }
    }
}

internal interface IProcessLifetime : IDisposable
{
    int Id { get; }
    Task<int> Exit { get; }
    void Terminate();
    Task ReapAsync(CancellationToken cancellationToken);
}
