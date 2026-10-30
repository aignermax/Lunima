using System.Diagnostics;
using System.Globalization;
using CAP_Core.Export.Processes;

namespace CAP.Avalonia.Services.AgentBridge.Registration;

/// <summary>Exit code and combined stdout/stderr of a finished CLI call.</summary>
/// <param name="ExitCode">Process exit code; <see cref="CliProcessRunner.TimedOutExitCode"/> on timeout,
/// <see cref="CliProcessRunner.StartFailedExitCode"/> when the process could not start.</param>
/// <param name="Output">Trimmed stdout followed by stderr.</param>
public sealed record CliRunResult(int ExitCode, string Output)
{
    /// <summary>Whether the call exited with code 0.</summary>
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// Runs a short-lived CLI process built by <see cref="CAP_Core.Export.ProcessLaunchFactory"/>
/// asynchronously (never on the UI thread's time), capturing its output and killing it when
/// it exceeds the timeout, so a hung CLI can never hang Lunima or an installer.
/// </summary>
public static class CliProcessRunner
{
    /// <summary>Exit code reported when the process was killed after the timeout.</summary>
    public const int TimedOutExitCode = -1;

    /// <summary>Exit code reported when the process could not be started at all.</summary>
    public const int StartFailedExitCode = -2;

    /// <summary>Starts <paramref name="startInfo"/> and waits for it, at most <paramref name="timeout"/>.</summary>
    public static async Task<CliRunResult> RunAsync(
        ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.RedirectStandardInput = true;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);

        OwnedProcess process;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prepared = WindowsBatchCommand.Prepare(startInfo);
            cancellationToken.ThrowIfCancellationRequested();
            process = OwnedProcess.Start(prepared);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new CliRunResult(StartFailedExitCode, ex.Message);
        }

        await using var owner = process;
        // Pipe readers belong to this invocation, not to the canceled request token.
        // Retirement terminates the owned process group before awaiting their actual EOF.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var interrupted = false;
        try
        {
            process.StandardInput.Close();
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested) { interrupted = true; }
        finally { await RetireAsync(process, stdout, stderr); }

        cancellationToken.ThrowIfCancellationRequested();
        if (interrupted)
            return new CliRunResult(TimedOutExitCode,
                string.Create(CultureInfo.InvariantCulture, $"Timed out after {timeout.TotalSeconds:F0} s."));
        var output = ((await stdout).Trim() + Environment.NewLine + (await stderr).Trim()).Trim();
        cancellationToken.ThrowIfCancellationRequested();
        return new CliRunResult(process.ExitCode, output);
    }

    private static async Task RetireAsync(OwnedProcess process, Task<string> stdout, Task<string> stderr)
    {
        try { await process.StopAsync(); }
        catch
        {
            // A native retirement failure must be reported, but cannot leave readers abandoned.
            // Dispose closes the owned endpoints even when its repeated StopAsync reports failure.
            try { await process.DisposeAsync(); }
            finally
            {
                try { await Task.WhenAll(stdout, stderr); }
                catch { /* Observe pipe closure errors while preserving the retirement failure. */ }
            }
            throw;
        }
        await Task.WhenAll(stdout, stderr);
    }
}
