using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using CAP.Avalonia.Services.AgentBridge.Hosting;
using CAP.Avalonia.Services.AgentBridge.Protocol;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AgentBridge.Transport;
using CAP.Avalonia.Services.AiTools;

namespace UnitTests.AgentBridge;

internal sealed class BridgeLifecycleTestHarness : IAsyncDisposable
{
    internal static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);
    private readonly HttpClient _client = new() { Timeout = Guard };
    internal McpHttpServer Server { get; }
    internal AgentToolCatalog Catalog { get; }

    internal BridgeLifecycleTestHarness(IAgentBridgeTool tool, IUiDispatcher? dispatcher = null,
        Action<Exception>? reportCancellationError = null)
    {
        Catalog = new AgentToolCatalog([tool], new AiToolRegistry([]), dispatcher ?? new InlineUiDispatcher());
        Server = new McpHttpServer(new McpHttpEndpoint(new McpJsonRpcDispatcher(Catalog, "test"), () => null),
            reportCancellationError);
        Server.Start(0);
    }

    internal async Task CallAsync()
    {
        using var body = new StringContent("""
            {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"lifecycle_probe","arguments":{}}}
            """, Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync($"http://127.0.0.1:{Server.Port}/mcp", body);
    }

    internal static async Task ObserveRequestAsync(Task request)
    {
        try { await request.WaitAsync(Guard); }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await Server.StopAsync().WaitAsync(Guard);
    }
}

internal sealed class LifecycleProbeTool(Func<Task> work) : IAgentBridgeTool
{
    public string Name => "lifecycle_probe";
    public string Description => "Controlled lifecycle probe.";
    public object InputSchema => new { type = "object" };

    public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        await work();
        return AgentToolResult.Text("done");
    }
}

internal sealed class ContextLifecycleProbeTool(Func<AgentToolContext, Task> work) : IAgentBridgeTool
{
    public string Name => "lifecycle_probe";
    public string Description => "Controlled context-aware lifecycle probe.";
    public object InputSchema => new { type = "object" };

    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) =>
        throw new InvalidOperationException("Catalog must supply the operation context.");

    public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context)
    {
        await work(context);
        return AgentToolResult.Text("done");
    }
}

internal sealed class QueuedBridgeDispatcher : IUiDispatcher
{
    private readonly ConcurrentQueue<Func<Task>> _queued = new();
    internal TaskCompletionSource Queued { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<T> InvokeAsync<T>(Func<Task<T>> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queued.Enqueue(async () =>
        {
            try { completion.SetResult(await work()); }
            catch (Exception error) { completion.SetException(error); }
        });
        Queued.TrySetResult();
        return completion.Task;
    }

    internal async Task DrainAsync()
    {
        while (_queued.TryDequeue(out var work))
            await work().WaitAsync(BridgeLifecycleTestHarness.Guard);
    }
}
