using CAP.Avalonia.ViewModels.Export.PythonEnvironmentManager;
using CAP_Core.Export;
using CAP_Core.Export.PythonEnvironmentManager;
using Shouldly;

namespace UnitTests.Export.PythonEnvironmentManager;

public sealed class PythonCancellationOrderingTests
{
    [Fact]
    public async Task Retirement_PreventsSuccessWhileLinkedCancellationCallbackIsStillQueued()
    {
        var path = Path.Combine(Path.GetTempPath(), "python-order-" + Guid.NewGuid() + ".json");
        var install = new Installer();
        using var discovery = new Discovery();
        await using var model = new PythonEnvironmentManagerViewModel(new PythonEnvironmentRegistry(path),
            new UvBootstrapper(), new NazcaPackageInstaller(), new EnvironmentHealthChecker(discovery),
            discovery, provisioner: install);
        var installing = model.CreateAndInstallCommand.ExecuteAsync(null);
        var discovering = Task.CompletedTask;
        var stopping = Task.CompletedTask;
        try
        {
            await install.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Registered after the install's linked CTS: token callbacks run newest first.
            discovering = model.RefreshInterpretersAsync();
            await discovery.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stopping = model.StopAsync();
            await discovery.CancellationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            install.Cancelled.Task.IsCompleted.ShouldBeFalse("the actual linked token has not received cancellation yet");
            install.Release.TrySetResult();
            await installing.WaitAsync(TimeSpan.FromSeconds(5));
            model.ProgressText.Contains("is ready", StringComparison.Ordinal).ShouldBeFalse(
                "terminal ownership must guard publication independently of token callback order");
            stopping.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            discovery.ReleaseCancellation.Set();
            discovery.Release.TrySetResult();
            install.Release.TrySetResult();
            await Task.WhenAll(installing, discovering, stopping).WaitAsync(TimeSpan.FromSeconds(5));
            File.Delete(path);
        }
    }

    private sealed class Installer : IManagedPythonProvisioner
    {
        public TaskCompletionSource Started { get; } = Signal();
        public TaskCompletionSource Release { get; } = Signal();
        public TaskCompletionSource Cancelled { get; } = Signal();
        public Task<bool> IsReadyAsync(PythonEnvironment environment, CancellationToken ct) => Task.FromResult(false);
        public async Task ProvisionAsync(PythonEnvironment environment, string version, IProgress<string> progress, CancellationToken ct)
        {
            using var registration = ct.Register(() => Cancelled.TrySetResult());
            Started.TrySetResult();
            await Release.Task;
        }
    }

    private sealed class Discovery : PythonDiscoveryService, IDisposable
    {
        public TaskCompletionSource Started { get; } = Signal();
        public TaskCompletionSource Release { get; } = Signal();
        public TaskCompletionSource CancellationEntered { get; } = Signal();
        public ManualResetEventSlim ReleaseCancellation { get; } = new();
        public override async Task<List<PythonInstallation>> DiscoverPythonWithNazcaAsync(CancellationToken ct = default)
        {
            using var registration = ct.Register(() =>
            {
                CancellationEntered.TrySetResult();
                ReleaseCancellation.Wait(TimeSpan.FromSeconds(10));
            });
            Started.TrySetResult();
            await Release.Task;
            return new();
        }
        public void Dispose() => ReleaseCancellation.Dispose();
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
