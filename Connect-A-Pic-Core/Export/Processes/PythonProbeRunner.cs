using System.Diagnostics;

namespace CAP_Core.Export.Processes;

/// <summary>Captures Python probes or validation scripts, retiring their owned process group before completing.</summary>
public static class PythonProbeRunner
{
    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var result = await OwnedProcessCapture.RunAsync(startInfo, timeout, cancellationToken).ConfigureAwait(false);
        return result.TimedOut ? (-1, string.Empty, "Probe timed out.")
            : (result.ExitCode, result.Output, result.Error);
    }
}
