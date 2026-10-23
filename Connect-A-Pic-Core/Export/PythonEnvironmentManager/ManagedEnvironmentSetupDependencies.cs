namespace CAP_Core.Export.PythonEnvironmentManager;

// Infrastructure-only operation seams; presentation sees the capability workflow, never uv commands or paths.
internal interface IManagedRuntimeInstaller
{
    Task<string> EnsureUvAsync(IProgress<string> progress, CancellationToken token);
    Task CreateVenvAsync(string uv, string path, string version, IProgress<string> progress, CancellationToken token);
}

internal interface IManagedPackageInstaller
{
    Task InstallNazcaAsync(string uv, string path, IProgress<string> progress, CancellationToken token);
    Task InstallGdsFactoryAsync(string uv, string path, IProgress<string> progress, CancellationToken token);
    Task InstallBackendAsync(string uv, string path, string package, IProgress<string> progress, CancellationToken token);
}

internal sealed class ManagedRuntimeInstaller(UvBootstrapper bootstrapper) : IManagedRuntimeInstaller
{
    public Task<string> EnsureUvAsync(IProgress<string> progress, CancellationToken token) => bootstrapper.EnsureUvAsync(progress, token);
    public Task CreateVenvAsync(string uv, string path, string version, IProgress<string> progress, CancellationToken token) =>
        bootstrapper.CreateVenvAsync(uv, path, version, progress, token);
}

internal sealed class ManagedPackageInstaller(NazcaPackageInstaller installer) : IManagedPackageInstaller
{
    public Task InstallNazcaAsync(string uv, string path, IProgress<string> progress, CancellationToken token) =>
        installer.InstallAsync(uv, path, progress, token);
    public Task InstallGdsFactoryAsync(string uv, string path, IProgress<string> progress, CancellationToken token) =>
        installer.InstallGdsFactoryAsync(uv, path, progress, token);
    public Task InstallBackendAsync(string uv, string path, string package, IProgress<string> progress, CancellationToken token) =>
        installer.InstallPackageAsync(uv, path, package, progress, token);
}
