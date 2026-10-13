using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CAP.Avalonia.Services.AgentBridge.Protocol;
using Shouldly;
using Xunit;

namespace UnitTests.AgentBridge;

/// <summary>
/// JSON-RPC / MCP method handling of the agent bridge, independent of the HTTP transport.
/// </summary>
public class McpJsonRpcDispatcherTests
{
    private static async Task<JsonElement> Call(string json, McpJsonRpcDispatcher? dispatcher = null)
    {
        dispatcher ??= AgentBridgeTestStack.Dispatcher(new FixedAiTool("get_grid_state", "grid"));
        var reply = await dispatcher.DispatchAsync(AgentBridgeTestStack.Parse(json), CancellationToken.None);
        reply.ShouldNotBeNull();
        return AgentBridgeTestStack.Parse(reply);
    }

    [Fact]
    public async Task Initialize_EchoesSupportedVersion_AndAdvertisesTools()
    {
        var reply = await Call("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""");

        reply.GetProperty("jsonrpc").GetString().ShouldBe("2.0");
        reply.GetProperty("id").GetInt32().ShouldBe(1);
        var result = reply.GetProperty("result");
        result.GetProperty("protocolVersion").GetString().ShouldBe("2025-03-26");
        result.GetProperty("capabilities").GetProperty("tools").ValueKind.ShouldBe(JsonValueKind.Object);
        result.GetProperty("serverInfo").GetProperty("name").GetString().ShouldBe("lunima");
        result.GetProperty("serverInfo").GetProperty("version").GetString().ShouldBe("1.2.3");
    }

    [Fact]
    public async Task Initialize_UnknownVersion_FallsBackToLatest()
    {
        var reply = await Call("""{"jsonrpc":"2.0","id":"a","method":"initialize","params":{"protocolVersion":"1999-01-01"}}""");

        reply.GetProperty("id").GetString().ShouldBe("a");
        reply.GetProperty("result").GetProperty("protocolVersion").GetString()
            .ShouldBe(McpJsonRpcDispatcher.SupportedProtocolVersions[0]);
    }

    [Fact]
    public async Task ToolsList_ContainsBridgeAndAssistantTools_WithSchemas()
    {
        var reply = await Call("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");

        var tools = reply.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
        var names = tools.Select(t => t.GetProperty("name").GetString()).ToList();
        names.ShouldContain("echo");
        names.ShouldContain("get_grid_state", "assistant tools from the shared registry are exposed too");
        tools.ShouldAllBe(t => t.GetProperty("inputSchema").GetProperty("type").GetString() == "object");
    }

    [Fact]
    public async Task ToolsCall_ReturnsTextContent()
    {
        var reply = await Call("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"echo","arguments":{"text":"hi"}}}""");

        var result = reply.GetProperty("result");
        result.GetProperty("isError").GetBoolean().ShouldBeFalse();
        var block = result.GetProperty("content")[0];
        block.GetProperty("type").GetString().ShouldBe("text");
        block.GetProperty("text").GetString().ShouldBe("hi");
    }

    [Fact]
    public async Task ToolsCall_AssistantTool_RunsThroughAdapter()
    {
        var reply = await Call("""{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"get_grid_state"}}""");

        reply.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString().ShouldBe("grid");
    }

    [Fact]
    public async Task ToolsCall_UnknownTool_IsInvalidParamsError()
    {
        var reply = await Call("""{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"nope","arguments":{}}}""");

        var error = reply.GetProperty("error");
        error.GetProperty("code").GetInt32().ShouldBe(JsonRpcErrorCodes.InvalidParams);
        error.GetProperty("message").GetString()!.ShouldContain("nope");
    }

    [Fact]
    public async Task ToolsCall_ToolThrows_IsToolErrorResult_NotProtocolError()
    {
        var reply = await Call("""{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"echo","arguments":{"throw":true}}}""");

        var result = reply.GetProperty("result");
        result.GetProperty("isError").GetBoolean().ShouldBeTrue();
        result.GetProperty("content")[0].GetProperty("text").GetString()!.ShouldContain("boom");
    }

    [Fact]
    public async Task UnknownMethod_IsMethodNotFound()
    {
        var reply = await Call("""{"jsonrpc":"2.0","id":7,"method":"resources/list"}""");

        reply.GetProperty("error").GetProperty("code").GetInt32().ShouldBe(JsonRpcErrorCodes.MethodNotFound);
    }

    [Fact]
    public async Task Ping_ReturnsEmptyResult()
    {
        var reply = await Call("""{"jsonrpc":"2.0","id":8,"method":"ping"}""");

        reply.GetProperty("result").ValueKind.ShouldBe(JsonValueKind.Object);
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","method":"notifications/initialized"}""")]
    [InlineData("""{"jsonrpc":"2.0","id":9,"result":{}}""")]
    public async Task NotificationsAndClientResponses_GetNoReply(string json)
    {
        var dispatcher = AgentBridgeTestStack.Dispatcher();

        var reply = await dispatcher.DispatchAsync(AgentBridgeTestStack.Parse(json), CancellationToken.None);

        reply.ShouldBeNull();
    }

    [Fact]
    public async Task NonObjectMessage_IsInvalidRequest()
    {
        var reply = await Call("42");

        reply.GetProperty("error").GetProperty("code").GetInt32().ShouldBe(JsonRpcErrorCodes.InvalidRequest);
    }
}
