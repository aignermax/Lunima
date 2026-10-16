using System.Text.Json;

namespace CAP_Core.Export.PythonEnvironmentManager;

/// <summary>Shared first-start/Settings setup: managed Python plus the complete preview/export package set.</summary>
public sealed class ManagedPythonProvisioner(UvBootstrapper bootstrapper, NazcaPackageInstaller installer,
    ProcessLaunchFactory launchFactory) : IManagedPythonProvisioner
{
    private readonly SemaphoreSlim _installGate = new(1, 1);
    private const string Marker = "LUNIMA_PYTHON_READY:";
    // Imports, not package metadata: a partially installed dependency must never be activated.
    private const string Probe = "import sys,json,nazca,pyclipper,klayout.db,siepic_ebeam_pdk,gdsfactory,ubcpdk,cspdk; " +
        "print('LUNIMA_PYTHON_READY:'+json.dumps({'PythonVersion':sys.version.split()[0]," +
        "'NazcaVersion':str(getattr(nazca,'__version__','installed'))," +
        "'GdsFactoryVersion':str(getattr(gdsfactory,'__version__','installed'))," +
        "'CspdkVersion':str(getattr(cspdk,'__version__','installed'))}))";

    internal static async Task<bool> HasUsableRuntimeAsync(string executable, ProcessLaunchFactory factory,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(executable)) return false;
        try
        {
            var (code, output, _) = await UvBootstrapper.RunProcessAsync(factory, executable,
                ["-c", "import sys; print('LUNIMA_RUNTIME_READY')"], token, timeoutMs: 30_000).ConfigureAwait(false);
            return code == 0 && output.Split('\n').Any(l => l.Trim() == "LUNIMA_RUNTIME_READY");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return false; }
    }

    public Task<bool> IsReadyAsync(PythonEnvironment environment, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(environment.PythonExecutable)) return false;
            try
            {
                var (code, output, error) = await UvBootstrapper.RunProcessAsync(launchFactory,
                    environment.PythonExecutable, ["-c", Probe], cancellationToken, timeoutMs: 90_000);
                return ApplyHealthResult(environment, code, output, error);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return ApplyHealthResult(environment, -1, "", ex.Message); }
        }, cancellationToken);

    public async Task ProvisionAsync(PythonEnvironment environment, string version, IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        await _installGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(async () =>
            {
                if (!EnvironmentNaming.IsInsideDirectory(UvBootstrapper.EnvironmentsBaseDir, environment.VenvPath))
                    throw new InvalidOperationException("Only Lunima-managed environments can be provisioned automatically.");
                environment.Status = PythonEnvironmentStatus.Creating;
                var uv = await bootstrapper.EnsureUvAsync(progress, cancellationToken).ConfigureAwait(false);
                if (!await HasUsableRuntimeAsync(environment.PythonExecutable, launchFactory, cancellationToken).ConfigureAwait(false))
                {
                    var runtimeVersion = EnvironmentNaming.IsValidPythonVersion(environment.PythonVersion)
                        ? environment.PythonVersion! : version;
                    await bootstrapper.CreateVenvAsync(uv, environment.VenvPath, runtimeVersion, progress, cancellationToken).ConfigureAwait(false);
                }
                environment.Status = PythonEnvironmentStatus.Installing;
                await installer.InstallAsync(uv, environment.VenvPath, progress, cancellationToken).ConfigureAwait(false);
                await installer.InstallGdsFactoryAsync(uv, environment.VenvPath, progress, cancellationToken).ConfigureAwait(false);
                if (!await IsReadyAsync(environment, cancellationToken).ConfigureAwait(false))
                    throw new InvalidOperationException(environment.LastError ?? "Python package verification failed.");
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _installGate.Release(); }
    }

    internal static bool ApplyHealthResult(PythonEnvironment environment, int exitCode, string output, string error)
    {
        var line = output.Split('\n').LastOrDefault(l => l.StartsWith(Marker, StringComparison.Ordinal));
        if (exitCode == 0 && line != null)
        {
            try
            {
                using var json = JsonDocument.Parse(line[Marker.Length..]);
                environment.PythonVersion = json.RootElement.GetProperty("PythonVersion").GetString();
                environment.NazcaVersion = json.RootElement.GetProperty("NazcaVersion").GetString();
                environment.GdsFactoryVersion = json.RootElement.GetProperty("GdsFactoryVersion").GetString();
                environment.CspdkVersion = json.RootElement.GetProperty("CspdkVersion").GetString();
                environment.HasPyclipper = true;
                environment.Status = PythonEnvironmentStatus.Healthy;
                environment.LastError = null;
                return true;
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            { error = ex.Message; }
        }
        environment.Status = PythonEnvironmentStatus.Broken;
        environment.LastError = string.IsNullOrWhiteSpace(error) ? "Required Python packages could not be imported." : error.Trim();
        return false;
    }
}
