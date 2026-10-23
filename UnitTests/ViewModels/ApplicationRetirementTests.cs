using CAP.Avalonia.Services.Lifetime;
using Shouldly;

namespace UnitTests.ViewModels;

public sealed class ApplicationRetirementTests
{
    [Fact]
    public async Task Retirement_ClosesAdmissionAndStopsShellBeforeDisposingEitherOwner()
    {
        var admission = Signal();
        var shell = new ControlledShell();
        var admissionCalls = 0;
        var containerDisposals = 0;
        var lifetime = new ApplicationLifetimeCoordinator(shell,
            () => { admissionCalls++; return admission.Task; },
            () => { containerDisposals++; return ValueTask.CompletedTask; });
        var stopping = lifetime.StopAsync();
        try
        {
            admissionCalls.ShouldBe(1);
            shell.Stops.ShouldBe(1);
            shell.Disposals.ShouldBe(0);
            containerDisposals.ShouldBe(0);
            stopping.IsCompleted.ShouldBeFalse();
            shell.Release.TrySetResult();
            await Task.Yield();
            shell.Disposals.ShouldBe(0, "accepted bridge work still owns the shell");
        }
        finally { admission.TrySetResult(); shell.Release.TrySetResult(); await stopping; }
        shell.Disposals.ShouldBe(1);
        containerDisposals.ShouldBe(1);
        await lifetime.StopAsync();
        containerDisposals.ShouldBe(1);
    }

    [Fact]
    public async Task Retirement_IsTerminalForActivation()
    {
        var shell = new ControlledShell();
        shell.Release.TrySetResult();
        var lifetime = new ApplicationLifetimeCoordinator(shell, () => Task.CompletedTask,
            () => ValueTask.CompletedTask);
        await lifetime.StopAsync();
        await lifetime.ActivateAsync();
        shell.Activations.ShouldBe(0);
    }

    private sealed class ControlledShell : IApplicationShellLifetime
    {
        public TaskCompletionSource Release { get; } = Signal();
        public int Stops, Disposals, Activations;
        public Task ActivateAsync(CancellationToken cancellationToken = default) { Activations++; return Task.CompletedTask; }
        public Task StopAsync() { Stops++; return Release.Task; }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
