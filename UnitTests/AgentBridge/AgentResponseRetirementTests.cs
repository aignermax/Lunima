using System.Text.Json;
using CAP.Avalonia.Services.AgentBridge.Hosting;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AiTools;
using Shouldly;

namespace UnitTests.AgentBridge;

public sealed class AgentResponseRetirementTests
{
    [Theory]
    [InlineData("close", false)]
    [InlineData("cancel", false)]
    [InlineData("close", true)]
    [InlineData("cancel", true)]
    [InlineData("current", false)]
    [InlineData("current", true)]
    public async Task CompletedResponse_IsAdmittedAfterAsynchronousCleanup(string transition, bool error)
    {
        using var caller = new CancellationTokenSource();
        var cleanup = new DeferredCleanup();
        var tool = new ReturningTool(cleanup, error);
        var catalog = new AgentToolCatalog([tool], new AiToolRegistry([]), new InlineUiDispatcher());
        var request = catalog.InvokeAsync(tool.Name, AgentBridgeTestStack.Parse("{}"), caller.Token);
        Task stopping = Task.CompletedTask;
        try
        {
            await cleanup.Entered.Task.WaitAsync(BridgeLifecycleTestHarness.Guard);
            request.IsCompleted.ShouldBeFalse("accepted cleanup must drain before any response");
            if (transition == "close") stopping = catalog.CloseAdmissionAsync();
            if (transition == "cancel") await caller.CancelAsync();
            cleanup.Release.TrySetResult();

            if (transition == "current")
            {
                var result = await request.WaitAsync(BridgeLifecycleTestHarness.Guard);
                result.IsError.ShouldBe(error);
                result.Content.Single().Text.ShouldContain("completed");
            }
            else
                await Should.ThrowAsync<OperationCanceledException>(
                    () => request.WaitAsync(BridgeLifecycleTestHarness.Guard));
            cleanup.Finished.ShouldBeTrue();
        }
        finally
        {
            cleanup.Release.TrySetResult();
            try { await request.WaitAsync(BridgeLifecycleTestHarness.Guard); }
            catch (OperationCanceledException) { }
            await stopping.WaitAsync(BridgeLifecycleTestHarness.Guard);
            await catalog.CloseAdmissionAsync().WaitAsync(BridgeLifecycleTestHarness.Guard);
        }
    }

    private sealed class ReturningTool(DeferredCleanup cleanup, bool error) : IAgentBridgeTool
    {
        public string Name => "cleanup_probe";
        public string Description => "Returns only after owned cleanup drains.";
        public object InputSchema => new { type = "object" };
        public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) =>
            throw new InvalidOperationException("The catalog must supply permission.");
        public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context)
        {
            await using var owned = cleanup;
            context.ThrowIfStopped();
            if (error) throw new InvalidOperationException("completed failure");
            return AgentToolResult.Text("completed success");
        }
    }

    private sealed class DeferredCleanup : IAsyncDisposable
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Finished { get; private set; }
        public async ValueTask DisposeAsync()
        {
            Entered.TrySetResult();
            await Release.Task;
            Finished = true;
        }
    }
}
