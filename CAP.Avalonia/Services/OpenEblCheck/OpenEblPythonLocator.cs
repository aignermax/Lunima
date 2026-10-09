using System.Diagnostics;
using CAP_Core.Export;

namespace CAP.Avalonia.Services.OpenEblCheck;

/// <summary>
/// Locates a Python interpreter that can run the vendored openEBL check scripts:
/// first a Lunima managed environment (%LOCALAPPDATA%/Lunima/envs/*), then
/// python/python3 on PATH. A candidate qualifies when
/// <c>import klayout.db, siepic_ebeam_pdk, SiEPIC</c> succeeds on it.
/// </summary>
public sealed class OpenEblPythonLocator
{
    /// <summary>Import statement a candidate interpreter must satisfy to run both checks.</summary>
    internal const string RequiredImports = "import klayout.db, siepic_ebeam_pdk, SiEPIC";

    /// <summary>pip packages a user must install when no candidate qualifies.</summary>
    internal const string PipInstallHint =
        "pip install klayout siepic_ebeam_pdk SiEPIC-Tools";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    private readonly ProcessLaunchFactory _factory;

    /// <summary>Initializes the locator with the shared process-launch factory.</summary>
    public OpenEblPythonLocator(ProcessLaunchFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Returns the first interpreter that satisfies <see cref="RequiredImports"/>,
    /// or null when none does. Never throws — a missing or broken interpreter
    /// simply does not qualify.
    /// </summary>
    public async Task<string?> FindPythonAsync(CancellationToken cancellationToken = default)
    {
        foreach (var candidate in Candidates())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await ProbeAsync(candidate, cancellationToken).ConfigureAwait(false))
                return candidate;
        }
        return null;
    }

    /// <summary>True when the given interpreter satisfies <see cref="RequiredImports"/>.</summary>
    public async Task<bool> ProbeAsync(string python, CancellationToken cancellationToken = default)
    {
        if (!_factory.TryBuild(
                python, new[] { "-c", RequiredImports }, null, null, out var startInfo, out _))
            return false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
                return false;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return false;
            }
            return process.ExitCode == 0;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;   // not on PATH at all, or not startable
        }
    }

    private IEnumerable<string> Candidates()
    {
        var envs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lunima", "envs");
        if (Directory.Exists(envs))
        {
            foreach (var root in Directory.GetDirectories(envs))
            {
                yield return Path.Combine(root, "Scripts", "python.exe");
                yield return Path.Combine(root, "bin", "python");
            }
        }
        yield return "python";
        yield return "python3";
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
    }
}
