using CAP_Core.Export.Contracts;
using CAP_Core.Export.PythonEnvironmentManager;
using Shouldly;
using Xunit;

namespace UnitTests.Export.PythonEnvironmentManager;

public sealed class ManagedEnvironmentPackageSetupTests
{
    [Fact]
    public async Task NewBackendEnvironment_CreatesRuntimeThenNazcaBackendAndHealth()
    {
        var fixture = new SetupFixture();
        var result = await fixture.Service.EnsureBackendAsync(null, "EMpy", fixture.Progress, default);

        fixture.Calls.ShouldBe(["uv", "create:3.11", "nazca", "backend:EMpy", "health"]);
        result.Name.ShouldBe("nazca");
        result.VenvPath.ShouldBe(Path.Combine(fixture.Options.EnvironmentsRoot, "nazca"));
        result.Status.ShouldBe(PythonEnvironmentStatus.Healthy);
    }

    [Fact]
    public async Task ExistingGdsFactoryEnvironment_PreservesIdentityAndSkipsCreation()
    {
        var fixture = new SetupFixture();
        var selected = new PythonEnvironment { Name = "selected", VenvPath = Path.Combine(fixture.Options.EnvironmentsRoot, "selected") };
        var result = await fixture.Service.EnsureGdsFactoryAsync(selected, fixture.Progress, default);

        result.ShouldBeSameAs(selected);
        fixture.Calls.ShouldBe(["uv", "gdsfactory", "health"]);
    }

    [Fact]
    public async Task CancellationAfterUncooperativeCreation_DoesNotInstallOrPublishHealth()
    {
        var fixture = new SetupFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.AfterCreate = cancellation.Cancel;

        await Should.ThrowAsync<OperationCanceledException>(() =>
            fixture.Service.EnsureBackendAsync(null, "EMpy", fixture.Progress, cancellation.Token));

        fixture.Calls.ShouldBe(["uv", "create:3.11"]);
        fixture.ObservedTokens.ShouldAllBe(token => token == cancellation.Token);
    }

    [Fact]
    public async Task InstallFailure_PreservesErrorAndDoesNotProbeHealth()
    {
        var fixture = new SetupFixture { Failure = new InvalidOperationException("installer stderr") };
        var selected = new PythonEnvironment { Name = "selected", VenvPath = "managed" };

        var error = await Should.ThrowAsync<InvalidOperationException>(() =>
            fixture.Service.EnsureBackendAsync(selected, "EMpy", fixture.Progress, default));

        error.ShouldBeSameAs(fixture.Failure);
        fixture.Calls.ShouldBe(["uv", "backend:EMpy"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalCancellation_StopsNextStepEvenBeforeLinkedTokenReceivesIt(bool retirement)
    {
        var fixture = new SetupFixture();
        using var original = new CancellationTokenSource();
        fixture.AfterCreate = original.Cancel;
        // An uncancelled operation token models a linked registration still queued behind another callback.
        await Should.ThrowAsync<OperationCanceledException>(() => fixture.Service.EnsureBackendAsync(
            null, "EMpy", fixture.Progress, default, retirement ? default : original.Token,
            retirement ? original.Token : default));
        fixture.Calls.ShouldBe(["uv", "create:3.11"]);
    }

    private sealed class SetupFixture : IManagedRuntimeInstaller, IManagedPackageInstaller, IPythonEnvironmentHealth
    {
        internal List<string> Calls { get; } = [];
        internal List<CancellationToken> ObservedTokens { get; } = [];
        internal Action? AfterCreate { get; set; }
        internal Exception? Failure { get; init; }
        internal IProgress<string> Progress { get; } = new Progress<string>();
        internal ManagedPythonEnvironmentOptions Options { get; } = new(Path.Combine(Path.GetTempPath(), "managed-setup-tests"));
        internal ManagedEnvironmentPackageSetup Service => new(this, this, this, Options);

        public Task<string> EnsureUvAsync(IProgress<string> progress, CancellationToken token)
        {
            Observe("uv", token);
            return Task.FromResult("uv");
        }
        public Task CreateVenvAsync(string uv, string path, string version, IProgress<string> progress, CancellationToken token)
        {
            Observe("create:" + version, token);
            AfterCreate?.Invoke();
            return Task.CompletedTask;
        }
        public Task InstallNazcaAsync(string uv, string path, IProgress<string> progress, CancellationToken token)
        { Observe("nazca", token); return Task.CompletedTask; }
        public Task InstallGdsFactoryAsync(string uv, string path, IProgress<string> progress, CancellationToken token)
        { Observe("gdsfactory", token); return Task.CompletedTask; }
        public Task InstallBackendAsync(string uv, string path, string package, IProgress<string> progress, CancellationToken token)
        {
            Observe("backend:" + package, token);
            return Failure == null ? Task.CompletedTask : Task.FromException(Failure);
        }
        public Task<PythonEnvironment> CheckAsync(PythonEnvironment environment, CancellationToken cancellationToken = default)
        {
            Observe("health", cancellationToken);
            environment.Status = PythonEnvironmentStatus.Healthy;
            return Task.FromResult(environment);
        }
        private void Observe(string step, CancellationToken token) { Calls.Add(step); ObservedTokens.Add(token); }
    }
}
