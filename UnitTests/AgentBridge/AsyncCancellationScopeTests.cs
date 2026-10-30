using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services.Lifetime;
using Shouldly;

namespace UnitTests.AgentBridge;

public sealed class AsyncCancellationScopeTests
{
    [AvaloniaFact]
    public async Task Retirement_DrainsHeldCallbackWithoutBlockingDispatcher()
    {
        using var source = new CancellationTokenSource();
        var scope = new AsyncCancellationScope(source.Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var callback = scope.Token.Register(() => { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(10)); });
        var cancellation = source.CancelAsync();
        var retiring = Task.CompletedTask;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            retiring = scope.DisposeAsync().AsTask();
            var tick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(() => tick.TrySetResult());
            await tick.Task.WaitAsync(TimeSpan.FromSeconds(1));
            retiring.IsCompleted.ShouldBeFalse("the registered cancellation callback still owns cleanup");
        }
        finally
        {
            release.Set();
            await cancellation.WaitAsync(TimeSpan.FromSeconds(5));
            await scope.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            await retiring.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Retirement_ReportsCallbackFailureAfterDrainingIt()
    {
        using var source = new CancellationTokenSource();
        var scope = new AsyncCancellationScope(source.Token);
        var callbackRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var callback = scope.Token.Register(() => { callbackRan.TrySetResult(); throw new InvalidOperationException("controlled"); });
        await source.CancelAsync();
        await callbackRan.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var error = await Should.ThrowAsync<AggregateException>(() => scope.DisposeAsync().AsTask());
        error.InnerExceptions.ShouldHaveSingleItem().Message.ShouldBe("controlled");
    }
}
