namespace CAP_Core.Export.PythonEnvironmentManager;

/// <summary>
/// Checks the health of a managed Python environment by verifying that Python
/// is executable, Nazca is importable, and pyclipper is importable.
/// Reuses <see cref="PythonDiscoveryService.CheckPythonInstallation"/> for
/// Python and Nazca version detection.
/// </summary>
public class EnvironmentHealthChecker
{
    private readonly PythonDiscoveryService _discovery;

    /// <summary>Initialises a new health checker with a shared discovery service.</summary>
    /// <param name="discovery">Discovery service for probing interpreter capabilities.</param>
    public EnvironmentHealthChecker(PythonDiscoveryService discovery)
    {
        _discovery = discovery;
    }

    /// <summary>
    /// Probes the given environment and updates its status, version fields,
    /// and <see cref="PythonEnvironment.LastError"/> in place.
    /// </summary>
    /// <param name="env">Environment to check. Modified in place.</param>
    /// <returns>The same <paramref name="env"/> instance after updating.</returns>
    public async Task<PythonEnvironment> CheckAsync(PythonEnvironment env)
    {
        var pythonExe = env.PythonExecutable;

        if (!File.Exists(pythonExe))
        {
            MarkBroken(env, "Python executable not found at expected path.");
            return env;
        }

        var installation = await _discovery.CheckPythonInstallation(pythonExe, "Managed");
        if (installation == null)
        {
            MarkBroken(env, "Python executable exists but could not be queried for version.");
            return env;
        }

        env.PythonVersion = installation.PythonVersion;
        env.NazcaVersion = installation.NazcaVersion;

        if (!installation.HasNazca)
        {
            MarkBroken(env, "Nazca is not installed or cannot be imported.");
            return env;
        }

        env.HasPyclipper = await CheckPyclipperAsync(pythonExe);

        env.Status = PythonEnvironmentStatus.Healthy;
        env.LastError = null;
        return env;
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private static void MarkBroken(PythonEnvironment env, string reason)
    {
        env.Status = PythonEnvironmentStatus.Broken;
        env.LastError = reason;
    }

    private static async Task<bool> CheckPyclipperAsync(string pythonPath)
    {
        try
        {
            var (exitCode, _, _) = await UvBootstrapper.RunProcessAsync(
                pythonPath,
                "-c \"import pyclipper\"",
                CancellationToken.None,
                timeoutMs: 10_000);
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
