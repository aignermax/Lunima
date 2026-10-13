using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CAP.Avalonia.Services.AgentBridge.Transport;
using Shouldly;
using Xunit;

namespace UnitTests.AgentBridge;

/// <summary>
/// HTTP semantics and security gates of the agent bridge's <c>/mcp</c> endpoint:
/// bearer token, loopback Host/Origin (DNS rebinding), methods and status codes.
/// </summary>
public class McpHttpEndpointTests
{
    private const string Initialize = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""";

    private static readonly McpHttpEndpoint Endpoint =
        new(AgentBridgeTestStack.Dispatcher(), () => AgentBridgeTestStack.Token);

    private static Task<BridgeHttpResponse> Send(
        string body, string method = "POST", string path = "/mcp", string? token = AgentBridgeTestStack.Token,
        string host = "127.0.0.1:39271", string? origin = null)
    {
        var headers = new System.Collections.Generic.List<(string, string)> { ("Host", host) };
        if (token != null)
            headers.Add(("Authorization", "Bearer " + token));
        if (origin != null)
            headers.Add(("Origin", origin));
        var request = new BridgeHttpRequest(method, path, AgentBridgeTestStack.Headers(headers.ToArray()),
            Encoding.UTF8.GetBytes(body), keepAlive: true);
        return Endpoint.HandleAsync(request, CancellationToken.None);
    }

    [Fact]
    public async Task ValidRequest_Returns200Json()
    {
        var response = await Send(Initialize);

        response.StatusCode.ShouldBe(200);
        response.ContentType.ShouldBe("application/json");
        JsonDocument.Parse(response.Body).RootElement.GetProperty("result").ValueKind.ShouldBe(JsonValueKind.Object);
    }

    [Fact]
    public async Task MissingToken_Is401WithBearerChallenge()
    {
        var response = await Send(Initialize, token: null);

        response.StatusCode.ShouldBe(401);
        response.Headers["WWW-Authenticate"].ShouldStartWith("Bearer");
    }

    [Fact]
    public async Task TokenNotRequired_RequestWithoutAuthorization_Is200()
    {
        var open = new McpHttpEndpoint(AgentBridgeTestStack.Dispatcher(), () => null);
        var request = new BridgeHttpRequest("POST", "/mcp", AgentBridgeTestStack.Headers(("Host", "127.0.0.1:39271")),
            Encoding.UTF8.GetBytes(Initialize), keepAlive: true);

        (await open.HandleAsync(request, CancellationToken.None)).StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task TokenNotRequired_ForeignOrigin_IsStill403()
    {
        var open = new McpHttpEndpoint(AgentBridgeTestStack.Dispatcher(), () => null);
        var request = new BridgeHttpRequest("POST", "/mcp",
            AgentBridgeTestStack.Headers(("Host", "127.0.0.1:39271"), ("Origin", "https://evil.example")),
            Encoding.UTF8.GetBytes(Initialize), keepAlive: true);

        (await open.HandleAsync(request, CancellationToken.None)).StatusCode.ShouldBe(403);
    }

    [Fact]
    public async Task WrongToken_Is401()
    {
        var response = await Send(Initialize, token: "not-the-token");

        response.StatusCode.ShouldBe(401);
    }

    [Theory]
    [InlineData("evil.example.com")]
    [InlineData("192.168.1.20:39271")]
    public async Task NonLoopbackHost_Is403(string host)
    {
        var response = await Send(Initialize, host: host);

        response.StatusCode.ShouldBe(403);
    }

    [Fact]
    public async Task ForeignBrowserOrigin_Is403()
    {
        var response = await Send(Initialize, origin: "https://evil.example.com");

        response.StatusCode.ShouldBe(403);
    }

    [Fact]
    public async Task LoopbackOrigin_IsAllowed()
    {
        var response = await Send(Initialize, origin: "http://localhost:3000", host: "localhost:39271");

        response.StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task Get_Is405_BecauseNoSseStreamIsOffered()
    {
        var response = await Send("", method: "GET");

        response.StatusCode.ShouldBe(405);
        response.Headers["Allow"].ShouldBe("POST");
    }

    [Fact]
    public async Task OtherPath_Is404()
    {
        var response = await Send(Initialize, path: "/other");

        response.StatusCode.ShouldBe(404);
    }

    [Fact]
    public async Task InvalidJson_Is400WithParseError()
    {
        var response = await Send("{not json");

        response.StatusCode.ShouldBe(400);
        JsonDocument.Parse(response.Body).RootElement.GetProperty("error").GetProperty("code").GetInt32()
            .ShouldBe(-32700);
    }

    [Fact]
    public async Task Notification_Is202WithoutBody()
    {
        var response = await Send("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        response.StatusCode.ShouldBe(202);
        response.Body.ShouldBeEmpty();
    }

    [Fact]
    public async Task Batch_ReturnsArrayOfReplies()
    {
        var response = await Send("""[{"jsonrpc":"2.0","id":1,"method":"ping"},{"jsonrpc":"2.0","method":"notifications/initialized"},{"jsonrpc":"2.0","id":2,"method":"ping"}]""");

        response.StatusCode.ShouldBe(200);
        JsonDocument.Parse(response.Body).RootElement.GetArrayLength().ShouldBe(2);
    }
}
