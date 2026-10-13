using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CAP.Avalonia.Services.AgentBridge.Transport;
using Shouldly;
using Xunit;

namespace UnitTests.AgentBridge;

/// <summary>
/// End-to-end over a real loopback socket: the server on an ephemeral port, driven by
/// <see cref="HttpClient"/> exactly as an MCP Streamable-HTTP client would.
/// </summary>
public class McpHttpServerIntegrationTests : IAsyncLifetime
{
    private readonly McpHttpServer _server = new(
        new McpHttpEndpoint(AgentBridgeTestStack.Dispatcher(new FixedAiTool("get_grid_state", "grid")),
            () => AgentBridgeTestStack.Token));
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(30) };

    public Task InitializeAsync()
    {
        _server.Start(0);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _server.StopAsync();
    }

    private Uri EndpointUri => new($"http://127.0.0.1:{_server.Port}/mcp");

    private async Task<HttpResponseMessage> Post(string json, string? token = AgentBridgeTestStack.Token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointUri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        if (token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    [Fact]
    public void Server_BindsAnEphemeralLoopbackPort()
    {
        _server.IsRunning.ShouldBeTrue();
        _server.Port.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task InitializeThenToolsListThenCall_OverHttp()
    {
        using var init = await Post("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"it","version":"1"}}}""");
        init.StatusCode.ShouldBe(HttpStatusCode.OK);
        init.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");

        using var initialized = await Post("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        initialized.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        using var list = await Post("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement;
        listJson.GetProperty("result").GetProperty("tools").GetArrayLength().ShouldBe(2);

        using var call = await Post("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"echo","arguments":{"text":"über"}}}""");
        var callJson = JsonDocument.Parse(await call.Content.ReadAsStringAsync()).RootElement;
        callJson.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString().ShouldBe("über");
    }

    [Fact]
    public async Task WithoutToken_IsRejected401()
    {
        using var response = await Post("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", token: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnknownTool_OverHttp_IsJsonRpcError()
    {
        using var response = await Post("""{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"missing"}}""");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("error").GetProperty("code").GetInt32().ShouldBe(-32602);
    }

    [Fact]
    public async Task StopAsync_ClosesTheListener()
    {
        var port = _server.Port;
        await _server.StopAsync();

        _server.IsRunning.ShouldBeFalse();
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        await Should.ThrowAsync<HttpRequestException>(() => probe.GetAsync($"http://127.0.0.1:{port}/mcp"));
    }
}
