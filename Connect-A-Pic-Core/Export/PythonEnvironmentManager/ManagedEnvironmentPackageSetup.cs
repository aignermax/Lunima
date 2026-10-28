using CAP_Core.Export.Contracts;

namespace CAP_Core.Export.PythonEnvironmentManager;

/// <summary>Owns package setup sequencing; registry activation remains the caller's admitted publication.</summary>
public sealed class ManagedEnvironmentPackageSetup : IManagedEnvironmentPackages
{
    private readonly IManagedRuntimeInstaller _runtime;
    private readonly IManagedPackageInstaller _packages;
    private readonly IPythonEnvironmentHealth _health;
    private readonly ManagedPythonEnvironmentOptions _options;

    public ManagedEnvironmentPackageSetup(UvBootstrapper runtime, NazcaPackageInstaller packages,
        IPythonEnvironmentHealth health, ManagedPythonEnvironmentOptions options)
        : this(new ManagedRuntimeInstaller(runtime), new ManagedPackageInstaller(packages), health, options) { }

    internal ManagedEnvironmentPackageSetup(IManagedRuntimeInstaller runtime, IManagedPackageInstaller packages,
        IPythonEnvironmentHealth health, ManagedPythonEnvironmentOptions options)
    {
        _runtime = runtime;
        _packages = packages;
        _health = health;
        _options = options;
    }

    public Task<PythonEnvironment> EnsureGdsFactoryAsync(PythonEnvironment? environment, IProgress<string> progress,
        CancellationToken cancellationToken, CancellationToken callerCancellation = default, CancellationToken stopping = default) =>
        EnsureAsync(environment, null, progress, cancellationToken, callerCancellation, stopping);

    public Task<PythonEnvironment> EnsureBackendAsync(PythonEnvironment? environment, string packageSpec,
        IProgress<string> progress, CancellationToken cancellationToken,
        CancellationToken callerCancellation = default, CancellationToken stopping = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageSpec);
        return EnsureAsync(environment, packageSpec, progress, cancellationToken, callerCancellation, stopping);
    }

    private async Task<PythonEnvironment> EnsureAsync(PythonEnvironment? environment, string? backend,
        IProgress<string> progress, CancellationToken token, CancellationToken caller, CancellationToken stopping)
    {
        void CheckCancellation()
        {
            caller.ThrowIfCancellationRequested();
            stopping.ThrowIfCancellationRequested();
            token.ThrowIfCancellationRequested();
        }
        CheckCancellation();
        var uv = await _runtime.EnsureUvAsync(progress, token).ConfigureAwait(false);
        CheckCancellation();
        if (environment == null)
        {
            environment = new PythonEnvironment { Name = _options.DefaultEnvironmentName,
                VenvPath = Path.Combine(_options.EnvironmentsRoot, _options.DefaultEnvironmentName) };
            progress.Report($"Creating managed environment '{environment.Name}'...");
            await _runtime.CreateVenvAsync(uv, environment.VenvPath, _options.DefaultPythonVersion, progress, token).ConfigureAwait(false);
            CheckCancellation();
            await _packages.InstallNazcaAsync(uv, environment.VenvPath, progress, token).ConfigureAwait(false);
            CheckCancellation();
        }
        if (backend == null)
            await _packages.InstallGdsFactoryAsync(uv, environment.VenvPath, progress, token).ConfigureAwait(false);
        else
            await _packages.InstallBackendAsync(uv, environment.VenvPath, backend, progress, token).ConfigureAwait(false);
        CheckCancellation();
        await _health.CheckAsync(environment, token).ConfigureAwait(false);
        CheckCancellation();
        return environment;
    }
}
