using System.Diagnostics;
using System.Globalization;

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
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.RedirectStandardInput = true;

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new CliRunResult(StartFailedExitCode, ex.Message);
        }
        if (process == null)
            return new CliRunResult(StartFailedExitCode, "The process did not start.");

        using (process)
        {
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return new CliRunResult(TimedOutExitCode, string.Create(CultureInfo.InvariantCulture, $"Timed out after {timeout.TotalSeconds:F0} s."));
            }
            var output = ((await stdout).Trim() + Environment.NewLine + (await stderr).Trim()).Trim();
            return new CliRunResult(process.ExitCode, output);
        }
    }
}
