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

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new RunResult(Outcome.StartFailed, -1, string.Empty, string.Empty, ex.Message);
        }

        await process.StandardInput.WriteAsync(stdinPayload);
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = ReadStderrAsync(process, onStderrLine);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var timeoutTask = Task.Delay(timeout, cts.Token);
        var completed = await Task.WhenAny(Task.WhenAll(stdoutTask, stderrTask), timeoutTask);

        if (completed == timeoutTask || ct.IsCancellationRequested)
        {
            TryKill(process);
            var outcome = ct.IsCancellationRequested ? Outcome.Cancelled : Outcome.TimedOut;
            return new RunResult(outcome, -1, string.Empty, string.Empty, null);
        }

        cts.Cancel(); // stop the timeout delay
        await process.WaitForExitAsync(CancellationToken.None);
        return new RunResult(Outcome.Completed, process.ExitCode, await stdoutTask, await stderrTask, null);
    }

    /// <summary>
    /// Reads stderr line by line (Meep/tqdm progress uses '\r', which ReadLineAsync
    /// also treats as a line break), forwarding each line to <paramref name="onLine"/>
    /// while accumulating the full text for error reporting.
    /// </summary>
    private static async Task<string> ReadStderrAsync(Process process, Action<string>? onLine)
    {
        var sb = new System.Text.StringBuilder();
        string? line;
        while ((line = await process.StandardError.ReadLineAsync()) != null)
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
