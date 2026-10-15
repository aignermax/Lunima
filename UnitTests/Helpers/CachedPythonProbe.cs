using System.Collections.Concurrent;
using System.Diagnostics;

namespace UnitTests.Helpers;

/// <summary>
/// Process-wide memo of the "is python / package X available" probes the env-gated export
/// tests run before each test. The interpreter set cannot change while the test process
/// runs, so every probe is launched once per (interpreter, statement) instead of once per
/// test — a probe that imports nazca, sax or torch costs seconds on a CI runner.
/// </summary>
internal static class CachedPythonProbe
{
    private const int VersionProbeTimeoutMs = 5000;
    private const int ImportProbeTimeoutMs = 10000;

    private static readonly string[] PathCandidates = { "python", "python3" };

    private static readonly ConcurrentDictionary<string, Lazy<bool>> Results = new();

    /// <summary>The first of <c>python</c>/<c>python3</c> on PATH that runs <c>--version</c>, or null.</summary>
    public static string? FindPythonOnPath() =>
        PathCandidates.FirstOrDefault(candidate => Succeeds(candidate, VersionProbeTimeoutMs, "--version"));

    /// <summary>True when <paramref name="python"/> imports nazca.</summary>
    public static bool IsNazcaInstalled(string python) => CanRun(python, "import nazca");

    /// <summary>True when <paramref name="python"/> runs <c>-c <paramref name="statement"/></c> with exit code 0.</summary>
    public static bool CanRun(string python, string statement, int timeoutMs = ImportProbeTimeoutMs) =>
        Succeeds(python, timeoutMs, "-c", statement);

    private static bool Succeeds(string python, int timeoutMs, params string[] arguments)
    {
        var key = python + "\u001f" + string.Join("\u001f", arguments);
        return Results.GetOrAdd(key, _ => new Lazy<bool>(() => Probe(python, timeoutMs, arguments))).Value;
    }

    private static bool Probe(string python, int timeoutMs, string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(python)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
                psi.ArgumentList.Add(argument);
            using var process = Process.Start(psi);
            if (process == null) return false;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return false;
            }
            Task.WaitAll(stdout, stderr);
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
