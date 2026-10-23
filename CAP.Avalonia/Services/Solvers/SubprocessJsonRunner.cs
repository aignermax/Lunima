using System.Diagnostics;
using System.Text.Json;

namespace CAP.Avalonia.Services.Solvers;

/// <summary>
/// Runs an external process that takes a JSON request on stdin and emits a JSON
/// result on stdout, with timeout, cancellation, and trailing-JSON extraction.
/// Shared by the mode-solver (python) and FDTD (docker) bridges so the
/// subprocess plumbing lives in exactly one place.
/// </summary>
public static class SubprocessJsonRunner
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How a subprocess run ended.</summary>
    public enum Outcome
    {
        /// <summary>Process exited on its own (check <see cref="RunResult.ExitCode"/>).</summary>
        Completed,
        /// <summary>Killed after exceeding the timeout.</summary>
        TimedOut,
        /// <summary>Killed because the caller cancelled.</summary>
        Cancelled,
        /// <summary>Process could not be started (e.g. executable not found).</summary>
        StartFailed,
    }

    /// <summary>Outcome plus captured streams of a subprocess run.</summary>
    public readonly record struct RunResult(
        Outcome Outcome, int ExitCode, string Stdout, string Stderr, string? StartError);

    /// <summary>
    /// Starts the process described by <paramref name="startInfo"/>, writes
    /// <paramref name="stdinPayload"/> to its stdin, and captures stdout/stderr.
    /// Kills the process on timeout or cancellation.
    /// </summary>
    /// <param name="onStderrLine">
    /// Optional callback invoked for each stderr line as it arrives (used to surface
    /// live solver progress). Called on a background thread.
    /// </param>
    public static async Task<RunResult> RunAsync(
        ProcessStartInfo startInfo, string stdinPayload, TimeSpan timeout, CancellationToken ct,
        Action<string>? onStderrLine = null)
    {
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        // Meep/tqdm progress uses UTF-8 block glyphs; decode them correctly so the
        // status text isn't mojibake (the caller still strips the bar art for display).
        startInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
        startInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lifetime.CancelAfter(timeout);
        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new RunResult(Outcome.StartFailed, -1, string.Empty, string.Empty, ex.Message);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(lifetime.Token);
        var stderrTask = ReadStderrAsync(process, onStderrLine, lifetime.Token);
        var inputTask = WriteInputAsync(process, stdinPayload, lifetime.Token);
        var exitTask = process.WaitForExitAsync(lifetime.Token);
        Task[] operations = [stdoutTask, stderrTask, inputTask, exitTask];
        var completed = false;
        try
        {
            await AwaitOperationsAsync(operations, lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            completed = true;
            return new RunResult(Outcome.Completed, process.ExitCode,
                await stdoutTask, await stderrTask, null);
        }
        catch (Exception) when (lifetime.IsCancellationRequested)
        {
            var outcome = ct.IsCancellationRequested ? Outcome.Cancelled : Outcome.TimedOut;
            return new RunResult(outcome, -1, string.Empty, string.Empty, null);
        }
        finally
        {
            if (!completed)
                await StopAsync(process, lifetime, operations);
        }
    }

    private static async Task WriteInputAsync(Process process, string payload, CancellationToken ct)
    {
        await process.StandardInput.WriteAsync(payload.AsMemory(), ct);
        await process.StandardInput.FlushAsync(ct);
        process.StandardInput.Close();
    }

    private static async Task AwaitOperationsAsync(Task[] operations, CancellationToken ct)
    {
        var pending = operations.ToList();
        while (pending.Count > 0)
        {
            // WhenAll delays reporting a broken pipe until the still-running child exits.
            var finished = await Task.WhenAny(pending).WaitAsync(ct);
            await finished;
            pending.Remove(finished);
        }
    }

    private static async Task StopAsync(Process process, CancellationTokenSource lifetime, Task[] operations)
    {
        TryKill(process);
        lifetime.Cancel();
        using var cleanup = new CancellationTokenSource(CleanupTimeout);
        try { await Task.WhenAll(operations).WaitAsync(cleanup.Token); }
        catch (Exception) when (!cleanup.IsCancellationRequested)
        { /* Observe the canceled or failed pipe operations before disposing their streams. */ }
        await process.WaitForExitAsync(cleanup.Token);
    }

    /// <summary>
    /// Reads stderr line by line (Meep/tqdm progress uses '\r', which ReadLineAsync
    /// also treats as a line break), forwarding each line to <paramref name="onLine"/>
    /// while accumulating the full text for error reporting.
    /// </summary>
    private static async Task<string> ReadStderrAsync(
        Process process, Action<string>? onLine, CancellationToken ct)
    {
        var sb = new System.Text.StringBuilder();
        string? line;
        while ((line = await process.StandardError.ReadLineAsync(ct)) != null)
        {
            sb.AppendLine(line);
            if (onLine != null && line.Trim().Length > 0)
                onLine(line.Trim());
        }
        return sb.ToString();
    }

    /// <summary>
    /// Walks stdout from the bottom up and returns the first line that parses as
    /// a JSON object — robust against library log chatter printed before the result.
    /// </summary>
    public static string? ExtractTrailingJsonLine(string stdout)
    {
        var lines = stdout.Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || !trimmed.StartsWith('{')) continue;
            try
            {
                using var _ = JsonDocument.Parse(trimmed);
                return trimmed;
            }
            catch (JsonException) { /* keep looking */ }
        }
        return null;
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch { /* best-effort */ }
    }
}
