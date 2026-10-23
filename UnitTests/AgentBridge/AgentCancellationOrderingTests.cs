using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AiTools;
using Shouldly;

namespace UnitTests.AgentBridge;

public sealed class AgentCancellationOrderingTests
{
    [Fact]
    public async Task OriginalCallerCancellation_RevokesCommitBeforeLinkedCallbacksRun()
    {
        using var caller = new CancellationTokenSource();
        using var releaseCallback = new ManualResetEventSlim();
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishWorker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? rejected = null;
        var mutations = 0;
        var tool = new ContextLifecycleProbeTool(async context =>
        {
            entered.TrySetResult();
            await finishWorker.Task;
            try { context.Commit(() => mutations++); }
            catch (OperationCanceledException error) { rejected = error; }
            finally { attempted.TrySetResult(); }
        });
        var catalog = new AgentToolCatalog([tool], new AiToolRegistry([]), new InlineUiDispatcher());
        var running = catalog.InvokeAsync(tool.Name, AgentBridgeTestStack.Parse("{}"), caller.Token);
        Task cancellation = Task.CompletedTask;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Registered last, so this callback runs before the linked token's callback.
            using var registration = caller.Token.Register(() =>
            {
                callbackEntered.TrySetResult();
                releaseCallback.Wait(TimeSpan.FromSeconds(10));
            });
            cancellation = caller.CancelAsync();
            await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            finishWorker.TrySetResult();
            await attempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            mutations.ShouldBe(0);
            rejected.ShouldBeOfType<OperationCanceledException>();
            releaseCallback.Set();
            await cancellation.WaitAsync(TimeSpan.FromSeconds(5));
            await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            releaseCallback.Set();
            finishWorker.TrySetResult();
            await cancellation.WaitAsync(TimeSpan.FromSeconds(5));
            try { await running.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
            await catalog.CloseAdmissionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
