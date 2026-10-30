using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Contracts;
using CAP.Avalonia.Services.Update;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Export.PythonEnvironmentManager;
using CAP.Avalonia.ViewModels.Settings.AgentBridge;
using CAP.Avalonia.ViewModels.Shell;
using CAP.Avalonia.ViewModels.Update;
using CAP_Core;
using CAP_Core.Export.Contracts;
using CAP_Core.Export.PythonEnvironmentManager;
using CAP_DataAccess.Updates;
using Moq;
using Shouldly;

namespace UnitTests.ViewModels;

public sealed class ShellActivationOwnershipTests
{
    [AvaloniaFact]
    public async Task ReentrantActivation_JoinsTheAlreadyAdmittedStartupJob()
    {
        using var fixture = new ActivationServices();
        var release = new TaskCompletionSource<List<PythonInstallation>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var discovery = new Mock<IPythonDiscovery>();
        var searches = 0;
        discovery.Setup(value => value.DiscoverPythonWithNazcaAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) => { searches++; return release.Task; });
        var gds = new GdsExportViewModel(Mock.Of<IGdsExportSession>(), discovery.Object);
        var activation = fixture.Create(gds);
        Task? reentrant = null;
        var entered = false;
        gds.PropertyChanged += (_, args) =>
        {
            if (entered || args.PropertyName != nameof(gds.IsSearching) || !gds.IsSearching) return;
            entered = true;
            reentrant = activation.ActivateAsync();
        };
        var first = activation.ActivateAsync();
        Task stopping = Task.CompletedTask;
        try
        {
            entered.ShouldBeTrue();
            reentrant.ShouldBeSameAs(first, "admission must be visible before startup raises any property notifications");
            searches.ShouldBe(1);
            stopping = activation.StopAsync();
            stopping.IsCompleted.ShouldBeFalse("the real discovery task is still held");
        }
        finally
        {
            stopping = activation.StopAsync();
            release.TrySetResult([]);
            // The broken implementation admits two tasks; explicitly drain both in the RED case.
            await Task.WhenAll(first, reentrant ?? Task.CompletedTask, stopping).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [AvaloniaFact]
    public async Task FailedPythonRetirement_StillStopsPromptAndDrainsHeldEnvironmentBeforeFailing()
    {
        using var fixture = new ActivationServices();
        var environment = new TaskCompletionSource<PythonEnvironmentInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new Mock<IGdsExportSession>();
        backend.Setup(value => value.CheckPythonEnvironmentAsync(It.IsAny<CancellationToken>())).Returns(environment.Task);
        var gds = new GdsExportViewModel(backend.Object, Mock.Of<IPythonDiscovery>());
        gds.Initialize("controlled-python");

        var provisioned = Signal();
        var provisionRelease = Signal();
        var registry = new Mock<IPythonEnvironmentRegistry>();
        registry.Setup(value => value.GetAll()).Returns(Array.Empty<PythonEnvironment>());
        var provisioner = new Mock<IManagedPythonProvisioner>();
        provisioner.Setup(value => value.IsReadyAsync(It.IsAny<PythonEnvironment>(), It.IsAny<CancellationToken>()))
            .Returns((PythonEnvironment _, CancellationToken token) => { token.ThrowIfCancellationRequested(); return Task.FromResult(false); });
        CancellationTokenRegistration throwingCallback = default;
        provisioner.Setup(value => value.ProvisionAsync(It.IsAny<PythonEnvironment>(), It.IsAny<string>(),
                It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .Returns((PythonEnvironment _, string _, IProgress<string> _, CancellationToken token) =>
            {
                throwingCallback = token.Register(() => throw new InvalidOperationException("controlled startup retirement failure"));
                provisioned.TrySetResult();
                return provisionRelease.Task;
            });
        var python = new PythonStartupViewModel(registry.Object, fixture.Preferences, provisioner.Object,
            new ManagedPythonEnvironmentOptions(Path.Combine(Path.GetTempPath(), "lunima-startup-owned-test")));

        var promptCancelled = Signal();
        var promptRelease = new TaskCompletionSource<ClaudeRegistrationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new Mock<IAgentConnection>();
        CancellationTokenRegistration promptCallback = default;
        connection.Setup(value => value.ConnectAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken token) =>
        {
            promptCallback = token.Register(() => promptCancelled.TrySetResult());
            return promptRelease.Task;
        });
        var prompt = new ClaudeCodeConnectPromptViewModel(fixture.Preferences, connection.Object, "test");
        var connecting = prompt.ConnectCommand.ExecuteAsync(null);
        var activation = fixture.Create(gds, prompt, python);
        var running = activation.ActivateAsync();
        Task stopping = Task.CompletedTask;
        try
        {
            await provisioned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stopping = activation.StopAsync();
            provisionRelease.TrySetResult();
            await Should.ThrowAsync<AggregateException>(() => python.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            await Task.WhenAny(promptCancelled.Task, stopping).WaitAsync(TimeSpan.FromSeconds(5));

            promptCancelled.Task.IsCompleted.ShouldBeTrue("one failed child retirement must not skip the next child");
            stopping.IsCompleted.ShouldBeFalse("environment and prompt work still own cleanup despite the Python cancellation error");
            promptRelease.TrySetResult(new(true, true, "connected"));
            environment.TrySetResult(new PythonEnvironmentInfo());
            var error = await Should.ThrowAsync<AggregateException>(() => stopping.WaitAsync(TimeSpan.FromSeconds(10)));
            error.ToString().ShouldContain("controlled startup retirement failure");
            running.IsCompleted.ShouldBeTrue();
            connecting.IsCompleted.ShouldBeTrue();
        }
        finally
        {
            provisionRelease.TrySetResult();
            promptRelease.TrySetResult(new(true, true, "connected"));
            environment.TrySetResult(new PythonEnvironmentInfo());
            await ObserveRetirementAsync(activation.StopAsync());
            await ObserveRetirementAsync(python.StopAsync());
            await prompt.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(running, connecting).WaitAsync(TimeSpan.FromSeconds(10));
            await throwingCallback.DisposeAsync();
            await promptCallback.DisposeAsync();
        }
    }

    private static async Task ObserveRetirementAsync(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (AggregateException error) when (error.ToString().Contains("controlled startup retirement failure")) { }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ActivationServices : IDisposable
    {
        private readonly HttpClient _http = new();
        private readonly string _preferencesPath = Path.Combine(Path.GetTempPath(), $"activation-ownership-{Guid.NewGuid():N}.json");
        internal UserPreferencesService Preferences { get; }
        private readonly UpdateViewModel _updates;

        internal ActivationServices()
        {
            Preferences = new(_preferencesPath);
            _updates = new(new UpdateChecker(_http, "test", "unused"), new UpdateDownloader(_http),
                Preferences, Mock.Of<IUrlLauncher>(), Mock.Of<IInstaller>());
        }

        internal ShellActivationCoordinator Create(GdsExportViewModel gds,
            ClaudeCodeConnectPromptViewModel? prompt = null, PythonStartupViewModel? python = null) =>
            new(_updates, gds, prompt, python, new ErrorConsoleService(), (_, token) => Task.Delay(Timeout.Infinite, token));

        public void Dispose()
        {
            _http.Dispose();
            File.Delete(_preferencesPath);
        }
    }
}
