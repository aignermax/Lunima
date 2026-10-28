using System.Diagnostics;

namespace CAP_Core.Export.Processes;

/// <summary>Captured output from a retired tool invocation; timeout is distinct from a tool exit code.</summary>
public readonly record struct CapturedProcessResult(int ExitCode, string Output, string Error, bool TimedOut);

/// <summary>Captures a redirected command under one deadline and awaits its process scope and pipe cleanup.</summary>
public static class OwnedProcessCapture
{
    public static async Task<CapturedProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        await using var process = OwnedProcess.Start(startInfo);
        var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            // Retire ordinary descendants even when their root exits successfully;
            // otherwise inherited pipe handles can outlive the root indefinitely.
            await process.StopAsync().ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).WaitAsync(deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            return new(process.ExitCode, await stdout, await stderr, false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(-1, string.Empty, string.Empty, true);
        }
        finally
        {
            // No tree-kill callback runs on the thread requesting cancellation.
            // The caller cannot observe completion until the actual owner and
            // every reader have finished, including exceptional paths.
            await RetireAndDrainAsync(process, stdout, stderr, deadline.Token).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static async Task RetireAndDrainAsync(OwnedProcess process, Task<string> stdout,
        Task<string> stderr, CancellationToken deadline)
    {
        try { await process.StopAsync().ConfigureAwait(false); }
        catch
        {
            // Native retirement failure still closes the pipe endpoints. Observe
            // closure failures as well, while preserving the retirement failure.
            try { await process.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); }
                catch { }
            }
            throw;
        }
        try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
    }
}
