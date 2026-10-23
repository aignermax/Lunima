using CAP.Avalonia.ViewModels.Export.PythonEnvironmentManager;
using CAP_Core.Export.Contracts;
using CAP_Core.Export.PythonEnvironmentManager;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Export.PythonEnvironmentManager;

public sealed class PythonPackagePublicationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retirement_DrainsPackageWorkflowWithoutActivatingItsLateResult(bool backend)
    {
        var environment = new PythonEnvironment { Name = "selected", VenvPath = "managed" };
        var registry = new Mock<IPythonEnvironmentRegistry>();
        registry.Setup(x => x.GetAll()).Returns([environment]);
        registry.Setup(x => x.GetActive()).Returns(environment);
        var packages = new DelayedPackages(environment);
        await using var model = new PythonEnvironmentManagerViewModel(registry.Object, packages,
            new ManagedPythonEnvironmentOptions("managed"), Mock.Of<IPythonEnvironmentHealth>(),
            Mock.Of<IPythonDiscovery>(), Mock.Of<IManagedPythonProvisioner>());
        var progress = new Progress<string>();
        var running = backend ? model.EnsureBackendInstalledAsync("EMpy", progress, default)
            : model.EnsureGdsFactoryInstalledAsync(progress, default);
        Task stopping = Task.CompletedTask;
        try
        {
            await packages.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stopping = model.StopAsync();
            stopping.IsCompleted.ShouldBeFalse();
            packages.Release.TrySetResult();
            await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            registry.Verify(x => x.AddOrUpdate(It.IsAny<PythonEnvironment>()), Times.Never);
            registry.Verify(x => x.SetActive(It.IsAny<string>()), Times.Never);
        }
        finally
        {
            packages.Release.TrySetResult();
            try { await running.WaitAsync(TimeSpan.FromSeconds(5)); } catch (OperationCanceledException) { }
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class DelayedPackages(PythonEnvironment environment) : IManagedEnvironmentPackages
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<PythonEnvironment> EnsureGdsFactoryAsync(PythonEnvironment? target, IProgress<string> progress,
            CancellationToken token, CancellationToken callerCancellation = default, CancellationToken stopping = default) => RunAsync();
        public Task<PythonEnvironment> EnsureBackendAsync(PythonEnvironment? target, string package, IProgress<string> progress,
            CancellationToken token, CancellationToken callerCancellation = default, CancellationToken stopping = default) => RunAsync();
        private async Task<PythonEnvironment> RunAsync()
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return environment; // Deliberately completes after cancellation to exercise the actual publication guard.
        }
    }
}
