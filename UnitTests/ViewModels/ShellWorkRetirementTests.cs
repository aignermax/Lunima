using CAP.Avalonia.Services.Lifetime;
using Shouldly;

namespace UnitTests.ViewModels;

public sealed class ShellWorkRetirementTests
{
    [Fact]
    public async Task Stop_CancelsOwnedJobsAndWaitsForCleanup()
    {
        var scope = new ShellWorkScope();
        var cancelled = Signal();
        var cleanup = Signal();
        var running = scope.RunAsync(async token =>
        {
            using var registration = token.Register(() => cancelled.TrySetResult());
            await cleanup.Task;
        });
        var stopping = scope.StopAsync();
        try
        {
            await Task.WhenAny(cancelled.Task, Task.Delay(250));
            cancelled.Task.IsCompleted.ShouldBeTrue();
            stopping.IsCompleted.ShouldBeFalse();
        }
        finally { cleanup.TrySetResult(); await Task.WhenAll(running, stopping); }
    }

    [Fact]
    public async Task Stop_RejectsLaterJobs()
    {
        var scope = new ShellWorkScope();
        await scope.StopAsync();
        var ran = false;
        await scope.RunAsync(_ => { ran = true; return Task.CompletedTask; });
        ran.ShouldBeFalse();
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
