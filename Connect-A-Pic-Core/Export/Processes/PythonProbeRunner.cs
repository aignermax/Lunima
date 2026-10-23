using System.Diagnostics;

namespace CAP_Core.Export.Processes;

/// <summary>Runs a short interpreter probe, retiring its entire owned process group before completing.</summary>
internal static class PythonProbeRunner
{
    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        await using var process = OwnedProcess.Start(startInfo);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token);
            // A probe's descendants must not hold its redirected pipes open after the root exits.
            await process.StopAsync();
            var stdout = await output;
            var stderr = await error;
            cancellationToken.ThrowIfCancellationRequested();
            return (process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            await process.StopAsync();
            await Task.WhenAll(output, error);
            cancellationToken.ThrowIfCancellationRequested();
            return (-1, string.Empty, "Probe timed out.");
        }
    }
}
