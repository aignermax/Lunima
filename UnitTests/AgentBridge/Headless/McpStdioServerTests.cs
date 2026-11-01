using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CAP.Avalonia.Services.AgentBridge.Protocol;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AgentBridge.Transport;
using CAP.Avalonia.Services.AiTools;
using Shouldly;
using Xunit;

namespace UnitTests.AgentBridge.Headless;

/// <summary>The newline-delimited JSON-RPC stdio transport of the headless MCP server.</summary>
public class McpStdioServerTests
{
    private static async Task<JsonElement[]> Serve(string input, McpJsonRpcDispatcher? dispatcher = null)
    {
        var output = new StringWriter();
        var server = new McpStdioServer(dispatcher ?? AgentBridgeTestStack.Dispatcher(), new StringReader(input), output);
        await server.RunAsync(CancellationToken.None);
        return output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(AgentBridgeTestStack.Parse)
            .ToArray();
    }

    [Fact]
    public async Task EachRequest_GetsExactlyOneResponseLine()
    {
        var replies = await Serve(
            """{"jsonrpc":"2.0","id":1,"method":"ping"}""" + "\n" +
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"echo","arguments":{"text":"a \"quoted\" µm"}}}""" + "\n");

        replies.Length.ShouldBe(2);
        var call = replies.Single(r => r.GetProperty("id").GetInt32() == 2);
        call.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()
            .ShouldBe("a \"quoted\" µm");
    }

    [Fact]
    public async Task Notifications_AndBlankLines_GetNoResponse()
    {
        var replies = await Serve(
            "\n" + """{"jsonrpc":"2.0","method":"notifications/initialized"}""" + "\n\n");

        replies.ShouldBeEmpty();
    }

    [Fact]
    public async Task MalformedLine_AnswersParseError_AndKeepsServing()
    {
        var replies = await Serve("not json\n" + """{"jsonrpc":"2.0","id":7,"method":"ping"}""" + "\n");

        replies.Length.ShouldBe(2);
        replies.Any(r => r.TryGetProperty("error", out var e)
            && e.GetProperty("code").GetInt32() == JsonRpcErrorCodes.ParseError).ShouldBeTrue();
        replies.Any(r => r.TryGetProperty("id", out var id)
            && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 7).ShouldBeTrue();
    }

    [Fact]
    public async Task CancelledNotification_CancelsTheRequest_AndSuppressesItsResponse()
    {
        var waiting = new WaitingTool();
        var catalog = new AgentToolCatalog(new IAgentBridgeTool[] { waiting },
            new AiToolRegistry(Array.Empty<IAiTool>()), new InlineUiDispatcher());
        var pipe = new BlockingLineReader();
        var output = new StringWriter();
        var server = new McpStdioServer(new McpJsonRpcDispatcher(catalog, "1"), pipe, output);
        var serving = server.RunAsync(CancellationToken.None);

        pipe.Send("""{"jsonrpc":"2.0","id":"slow","method":"tools/call","params":{"name":"wait"}}""");
        await waiting.Started.WaitAsync(TimeSpan.FromSeconds(10));
        pipe.Send("""{"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":"slow"}}""");
        await waiting.Cancelled.WaitAsync(TimeSpan.FromSeconds(10));
        pipe.Close();
        await serving.WaitAsync(TimeSpan.FromSeconds(10));

        output.ToString().ShouldBeEmpty("a cancelled request must not be answered");
    }

    /// <summary>Waits until its call is cancelled.</summary>
    private sealed class WaitingTool : IAgentBridgeTool
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;
        public Task Cancelled => _cancelled.Task;
        public string Name => "wait";
        public string Description => "Waits for cancellation.";
        public object InputSchema => new { type = "object" };

        public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => throw new NotSupportedException();

        public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context)
        {
            _started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, context.CancellationToken); }
            catch (OperationCanceledException) { _cancelled.TrySetResult(); throw; }
            return AgentToolResult.Text("never");
        }
    }

    /// <summary>A reader fed line by line from the test, like a live stdin pipe.</summary>
    private sealed class BlockingLineReader : TextReader
    {
        private readonly System.Threading.Channels.Channel<string?> _lines =
            System.Threading.Channels.Channel.CreateUnbounded<string?>();

        public void Send(string line) => _lines.Writer.TryWrite(line);
        public new void Close() => _lines.Writer.TryWrite(null);

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            await _lines.Reader.ReadAsync(cancellationToken);
    }
}
