namespace UnitTests.PdkOffset;

/// <summary>One opt-in Python probe shared by preview subprocess tests.</summary>
internal static class NazcaPreviewPythonFixture
{
    // The tool environment cannot change mid-run, so the probe spawns its
    // subprocesses once per test process instead of once per calling test.
    private static readonly Lazy<string?> CachedWorkingPython3 = new(FindWorkingPython3Uncached);

    /// <summary>
    /// Resolves a working Python 3 path by running a minimal subprocess validation.
    /// Returns null when Python is not available or cannot execute scripts.
    /// </summary>
    internal static string? FindWorkingPython3() => CachedWorkingPython3.Value;

    private static string? FindWorkingPython3Uncached()
    {
        // CI uses actions/setup-python, which installs into /opt/hostedtoolcache
        // rather than /usr/bin — let the workflow point us at the right binary
        // via env var so the SiEPIC pip-installed packages are actually visible
        // to the subprocess. Falls back to the autodiscovery list otherwise.
        // Opt-in gate: these spawn real python (nazca) subprocesses, heavy enough to destabilise a
        // desktop session. They run only when the workflow points at a python (CI sets this);
        // a plain local `dotnet test` skips them.
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LUNIMA_TEST_PYTHON3")))
            return null;
        var envOverride = Environment.GetEnvironmentVariable("LUNIMA_TEST_PYTHON3");
        var candidates = !string.IsNullOrWhiteSpace(envOverride)
            ? new[] { envOverride, "/usr/bin/python3", "/usr/local/bin/python3", "python3" }
            : new[] { "/usr/bin/python3", "/usr/local/bin/python3", "python3" };
        foreach (var c in candidates)
        {
            try
            {
                // Validate that Python can actually execute a minimal inline script,
                // not just that the binary exists. This guards against broken venvs.
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = c,
                    Arguments = "-c \"import sys; print('ok')\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                p?.WaitForExit(5000);
                if (p?.ExitCode == 0) return c;
            }
            catch { /* try next */ }
        }
        return null;
    }

    internal static string? FindRealPreviewScript() =>
        UnitTests.Integration.GdsAlignmentTestSetup.FindRealPreviewScript();
}
