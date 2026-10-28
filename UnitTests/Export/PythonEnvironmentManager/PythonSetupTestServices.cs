using CAP.Avalonia.ViewModels.Export.PythonEnvironmentManager;
using CAP_Core.Export;
using CAP_Core.Export.Contracts;
using CAP_Core.Export.PythonEnvironmentManager;

namespace UnitTests.Export.PythonEnvironmentManager;

/// <summary>Explicit test composition for panels; production uses the registered shared services.</summary>
internal static class PythonSetupTestServices
{
    internal static PythonEnvironmentManagerViewModel Create(IPythonEnvironmentRegistry registry,
        IPythonDiscovery? discovery = null, IPythonEnvironmentHealth? health = null,
        IManagedPythonProvisioner? provisioner = null, PythonStartupViewModel? startup = null,
        Func<string?>? getActiveInterpreterPath = null)
    {
        var factory = ProcessLaunchFactory.CreateDefault();
        var runtime = new UvBootstrapper(factory);
        var installer = new NazcaPackageInstaller(factory);
        var options = new ManagedPythonEnvironmentOptions(UvBootstrapper.EnvironmentsBaseDir, UvBootstrapper.DefaultPythonVersion);
        health ??= new EnvironmentHealthChecker(new PythonDiscoveryService(), factory);
        return new(registry, new ManagedEnvironmentPackageSetup(runtime, installer, health, options), options, health,
            discovery ?? new PythonDiscoveryService(), provisioner ?? new ManagedPythonProvisioner(runtime, installer, factory),
            getActiveInterpreterPath, startup);
    }
}
