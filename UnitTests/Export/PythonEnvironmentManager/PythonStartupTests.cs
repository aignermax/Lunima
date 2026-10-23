using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Export.PythonEnvironmentManager;
using CAP_Core.Export.PythonEnvironmentManager;
using Shouldly;

namespace UnitTests.Export.PythonEnvironmentManager;

public sealed class PythonStartupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "python-startup-" + Guid.NewGuid().ToString("N"));
    private readonly UserPreferencesService _preferences;
    private readonly PythonEnvironmentRegistry _registry;
    private readonly FakePythonProvisioner _installer = new();

    public PythonStartupTests()
    {
        Directory.CreateDirectory(_root);
        _preferences = new UserPreferencesService(Path.Combine(_root, "prefs.json"));
        _registry = new PythonEnvironmentRegistry(Path.Combine(_root, "envs.json"));
        _registry.OnActiveEnvironmentChanged = _preferences.SetCustomPythonPath;
    }

    private PythonStartupViewModel Create() => new(_registry, _preferences, _installer,
        new CAP_Core.Export.Contracts.ManagedPythonEnvironmentOptions(_root));

    [Fact]
    public async Task SettingsCreation_DoesNotCompeteWithBackgroundSetup()
    {
        var startup = Create();
        var pending = startup.StartOnceAsync();
        var manager = PythonSetupTestServices.Create(_registry, provisioner: _installer, startup: startup);
        await manager.CreateAndInstallCommand.ExecuteAsync(null);
        _installer.Installs.ShouldBe(1);
        _registry.GetAll().Count().ShouldBe(1);
        _installer.Finish();
        await pending;
        manager.Interpreters.Single().IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task SettingsCreation_UsesSameProvisionerAndPackageHealthPipeline()
    {
        var manager = PythonSetupTestServices.Create(_registry, provisioner: _installer);
        var pending = manager.CreateAndInstallCommand.ExecuteAsync(null);
        _installer.Installs.ShouldBe(1);
        _installer.Finish();
        await pending;
        manager.IsBusy.ShouldBeFalse();
        _registry.GetAll().Count().ShouldBe(1);
    }

    [Fact]
    public async Task FreshProfile_StartsOnceInBackground_ActivatesOnlyAfterCompleteHealthCheck()
    {
        var vm = Create();
        int activations = 0;
        _preferences.PythonInterpreterActivated += () => activations++;
        var running = vm.StartOnceAsync();
        vm.IsRunning.ShouldBeTrue();
        running.IsCompleted.ShouldBeFalse();
        _registry.GetActive().ShouldBeNull();
        var duplicate = vm.StartOnceAsync();
        _installer.Installs.ShouldBe(1);
        _installer.Finish();
        await Task.WhenAll(running, duplicate);
        vm.IsRunning.ShouldBeFalse();
        vm.HasError.ShouldBeFalse();
        _registry.GetActive().ShouldNotBeNull();
        _preferences.GetCustomPythonPath().ShouldBe(_registry.GetActive()!.PythonExecutable);
        activations.ShouldBe(1);
    }

    [Fact]
    public async Task HealthyManagedEnvironment_IsReusedWithoutInstall()
    {
        var existing = new PythonEnvironment { Name = "existing", VenvPath = Path.Combine(_root, "existing") };
        _registry.AddOrUpdate(existing);
        _installer.Ready = true;
        await Create().StartOnceAsync();
        _installer.Installs.ShouldBe(0);
        _registry.GetActive().ShouldBeSameAs(existing);
    }

    [Fact]
    public async Task ExplicitExternalInterpreter_IsNeverModifiedOrReplaced()
    {
        var custom = Path.Combine(_root, "external", "python.exe");
        _preferences.SetCustomPythonPath(custom);
        await Create().StartOnceAsync();
        _preferences.GetCustomPythonPath().ShouldBe(custom);
        _registry.GetAll().ShouldBeEmpty();
        _installer.Installs.ShouldBe(0);
    }

    [Fact]
    public async Task FailedInstall_DoesNotActivate_AndExplicitRetryCanRecover()
    {
        var vm = Create();
        var attempt = vm.StartOnceAsync();
        _installer.Completion.SetException(new IOException("Offline"));
        await attempt;
        vm.HasError.ShouldBeTrue();
        vm.ErrorText.ShouldContain("Offline");
        _registry.GetActive().ShouldBeNull();
        _preferences.GetCustomPythonPath().ShouldBeNull();
        await vm.StartOnceAsync();
        _installer.Installs.ShouldBe(1);
        _installer.Completion = new();
        var retry = vm.RetryCommand.ExecuteAsync(null);
        _installer.Finish();
        await retry;
        _installer.Installs.ShouldBe(2);
        vm.HasError.ShouldBeFalse();
        _registry.GetActive().ShouldNotBeNull();
    }

    [Fact]
    public async Task Shutdown_CancelsInstallation_WithoutActivatingPartialEnvironment()
    {
        var vm = Create();
        var pending = vm.StartOnceAsync();
        vm.Cancel();
        await pending;
        _installer.LastToken.IsCancellationRequested.ShouldBeTrue();
        _registry.GetActive().ShouldBeNull();
        vm.IsRunning.ShouldBeFalse();
        vm.RetryCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task IncompletePackageHealth_NeverActivatesEvenIfInstallerReturnsSuccessfully()
    {
        var vm = Create();
        var pending = vm.StartOnceAsync();
        _installer.Completion.SetResult();
        await pending;
        vm.HasError.ShouldBeTrue();
        _registry.GetActive().ShouldBeNull();
    }

    [Fact]
    public async Task UserChangingInterpreterDuringInstall_IsNotOverriddenByCompletion()
    {
        var vm = Create();
        var pending = vm.StartOnceAsync();
        _preferences.SetCustomPythonPath("user-choice");
        _installer.Finish();
        await pending;
        _preferences.GetCustomPythonPath().ShouldBe("user-choice");
        _registry.GetActive().ShouldBeNull();
    }

    [Fact]
    public async Task NewAppStart_RetriesPreviousFailureWithoutPersistentAttemptFlag()
    {
        var first = Create().StartOnceAsync();
        _installer.Completion.SetException(new IOException("Offline"));
        await first;
        _installer.Completion = new();
        var second = Create().StartOnceAsync();
        _installer.Installs.ShouldBe(2);
        _installer.Finish();
        await second;
        _registry.GetActive().ShouldNotBeNull();
    }

    public void Dispose() => Directory.Delete(_root, true);
}

internal sealed class FakePythonProvisioner : IManagedPythonProvisioner
{
    public TaskCompletionSource Completion { get; set; } = new();
    public bool Ready { get; set; }
    public int Installs { get; private set; }
    public CancellationToken LastToken { get; private set; }
    public IProgress<string>? Progress { get; private set; }
    public Task<bool> IsReadyAsync(PythonEnvironment environment, CancellationToken cancellationToken) => Task.FromResult(Ready);
    public async Task ProvisionAsync(PythonEnvironment environment, string version, IProgress<string> progress, CancellationToken cancellationToken)
    {
        Installs++;
        LastToken = cancellationToken;
        Progress = progress;
        progress.Report("Installing preview dependencies");
        await Completion.Task.WaitAsync(cancellationToken);
    }
    public void Finish() { Ready = true; Completion.SetResult(); }
}
