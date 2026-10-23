using CAP_Core.Export.Contracts;
using CAP.Avalonia.ViewModels.Export.PythonEnvironmentManager;
using CAP_Core.Export;
using CAP_Core.Export.PythonEnvironmentManager;
using Shouldly;
using UnitTests.Solvers.Lifetime;

namespace UnitTests.Export.PythonEnvironmentManager;

public sealed class PythonPanelRetirementTests
{
    [Theory]
    [InlineData("install")]
    [InlineData("health")]
    [InlineData("discover")]
    public async Task Retirement_CancelsAndDrainsManualOperation(string operation)
    {
        using var fixture = new Fixture();
        var running = fixture.Start(operation);
        var stopping = Task.CompletedTask;
        try
        {
            await fixture.Barrier.AwaitStartAsync(running);
            stopping = fixture.Model.StopAsync();
            await fixture.Barrier.AssertDrainingAsync(stopping);
        }
        finally { await fixture.Barrier.FinishAsync(running, stopping); }

        fixture.Model.ProgressText.ShouldNotContain("is ready");
        fixture.Model.ProgressText.ShouldNotContain("Health check done");
        fixture.Model.IsBusy.ShouldBeFalse();
        fixture.Model.IsDiscoveringInterpreters.ShouldBeFalse();
    }

    [Theory]
    [InlineData("install")]
    [InlineData("health")]
    [InlineData("discover")]
    public async Task Retirement_RejectsNewManualOperations(string operation)
    {
        using var fixture = new Fixture();
        fixture.Barrier.Release.TrySetResult();
        await fixture.Model.StopAsync();

        await fixture.Start(operation);

        fixture.Barrier.Started.Task.IsCompleted.ShouldBeFalse();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "python-panel-" + Guid.NewGuid() + ".json");
        private readonly PythonEnvironmentRegistry _registry;
        public RetirementBarrier Barrier { get; } = new();
        public PythonEnvironmentManagerViewModel Model { get; }

        public Fixture()
        {
            _registry = new(_path);
            _registry.AddOrUpdate(new PythonEnvironment { Name = "existing", VenvPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()) });
            Model = new(_registry, new UvBootstrapper(), new NazcaPackageInstaller(), new Health(Barrier),
                new Discovery(Barrier), provisioner: new Provisioner(Barrier));
        }

        public Task Start(string operation) => operation switch
        {
            "install" => Model.CreateAndInstallCommand.ExecuteAsync(null),
            "health" => Model.CheckHealthInterpreterCommand.ExecuteAsync(Model.Interpreters.First()),
            _ => Model.RefreshInterpretersAsync(),
        };

        public void Dispose() => File.Delete(_path);
    }

    private sealed class Provisioner(RetirementBarrier barrier) : IManagedPythonProvisioner
    {
        public Task<bool> IsReadyAsync(PythonEnvironment environment, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task ProvisionAsync(PythonEnvironment environment, string version, IProgress<string> progress,
            CancellationToken cancellationToken) => barrier.WaitAsync(cancellationToken);
    }

    private sealed class Health(RetirementBarrier barrier) : EnvironmentHealthChecker(new PythonDiscoveryService())
    {
        public override async Task<PythonEnvironment> CheckAsync(PythonEnvironment environment, CancellationToken ct = default)
        { await barrier.WaitAsync(ct); return environment; }
    }

    private sealed class Discovery(RetirementBarrier barrier) : PythonDiscoveryService
    {
        public override async Task<List<PythonInstallation>> DiscoverPythonWithNazcaAsync(CancellationToken cancellationToken = default)
        { await barrier.WaitAsync(cancellationToken); return new(); }
    }
}
