using CAP.Avalonia.Services.Lifetime;
using Shouldly;

namespace UnitTests.ViewModels;

public sealed class ApplicationWorkRetirementTests
{
    [Fact]
    public async Task Stop_BeginsAllFeatureRetirementsAndWaitsForTheirCleanup()
    {
        var owner = new ApplicationWorkCoordinator();
        var first = Signal();
        var second = Signal();
        var calls = 0;
        using var a = owner.Register(() => { calls++; return first.Task; });
        using var b = owner.Register(() => { calls++; return second.Task; });
        var stopping = owner.StopAsync();
        try
        {
            calls.ShouldBe(2);
            stopping.IsCompleted.ShouldBeFalse();
            first.TrySetResult();
            await Task.Yield();
            stopping.IsCompleted.ShouldBeFalse("every feature must finish before container disposal");
        }
        finally { first.TrySetResult(); second.TrySetResult(); await stopping.WaitAsync(TimeSpan.FromSeconds(5)); }
        await owner.StopAsync();
        calls.ShouldBe(2);
    }

    [Fact]
    public async Task Stop_RejectsFeatureConstructionDuringDrainAndAfterCompletion()
    {
        var owner = new ApplicationWorkCoordinator();
        var release = Signal();
        using var registration = owner.Register(() => release.Task);
        var stopping = owner.StopAsync();
        try { Should.Throw<InvalidOperationException>(() => owner.Register(() => Task.CompletedTask)); }
        finally { release.TrySetResult(); await stopping.WaitAsync(TimeSpan.FromSeconds(5)); }
        Should.Throw<InvalidOperationException>(() => owner.Register(() => Task.CompletedTask));
    }

    [Fact]
    public async Task DisposedRegistration_DoesNotRetainClosedFeature()
    {
        var owner = new ApplicationWorkCoordinator();
        var calls = 0;
        var registration = owner.Register(() => { calls++; return Task.CompletedTask; });
        registration.Dispose();
        await owner.StopAsync();
        calls.ShouldBe(0);
    }

    [Fact]
    public async Task FaultingFeature_DoesNotPreventOtherFeaturesFromDraining()
    {
        var owner = new ApplicationWorkCoordinator();
        var release = Signal();
        var started = false;
        using var a = owner.Register(() => throw new InvalidOperationException("fixture failure"));
        using var b = owner.Register(() => { started = true; return release.Task; });
        var stopping = owner.StopAsync();
        try
        {
            started.ShouldBeTrue();
            stopping.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            release.TrySetResult();
            try { await stopping.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (InvalidOperationException) { }
        }
        stopping.IsFaulted.ShouldBeTrue();
    }

    [Fact]
    public async Task ApplicationRetirement_WaitsForFeaturesBeforeDisposingShellOrContainer()
    {
        var features = new ApplicationWorkCoordinator();
        var release = Signal();
        var stopped = false;
        var shell = new Shell();
        var disposedServices = false;
        using var registration = features.Register(() => { stopped = true; return release.Task; });
        var application = new ApplicationLifetimeCoordinator(shell, () => Task.CompletedTask,
            () => { disposedServices = true; return ValueTask.CompletedTask; }, features);
        var stopping = application.StopAsync();
        try
        {
            stopped.ShouldBeTrue();
            shell.Stopped.ShouldBeTrue();
            shell.Disposed.ShouldBeFalse();
            disposedServices.ShouldBeFalse();
        }
        finally { release.TrySetResult(); await stopping.WaitAsync(TimeSpan.FromSeconds(5)); }
        shell.Disposed.ShouldBeTrue();
        disposedServices.ShouldBeTrue();
    }

    private sealed class Shell : IApplicationShellLifetime
    {
        public bool Stopped, Disposed;
        public Task ActivateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() { Stopped = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
