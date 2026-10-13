using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CAP.Avalonia.Services.AgentBridge.Protocol;

namespace CAP.Avalonia.Services.AgentBridge.Transport;

/// <summary>
/// The MCP Streamable-HTTP endpoint (<c>POST /mcp</c>) of the agent bridge. Applies the
/// security gates — loopback Host/Origin (DNS-rebinding guard) and the per-install bearer
/// token — then hands JSON-RPC messages to the <see cref="McpJsonRpcDispatcher"/>.
/// Every request is answered with a single <c>application/json</c> body; the optional
/// server-to-client SSE stream (<c>GET</c>) is not offered, which the spec allows (405).
/// </summary>
public sealed class McpHttpEndpoint
{
    /// <summary>The only path the bridge serves.</summary>
    public const string EndpointPath = "/mcp";

    private const string BearerPrefix = "Bearer ";

    private readonly McpJsonRpcDispatcher _dispatcher;
    private readonly Func<string> _tokenProvider;

    /// <summary>Creates the endpoint.</summary>
    /// <param name="dispatcher">Handles the JSON-RPC payloads.</param>
    /// <param name="tokenProvider">Returns the bearer token every request must present.</param>
    public McpHttpEndpoint(McpJsonRpcDispatcher dispatcher, Func<string> tokenProvider)
    {
        _dispatcher = dispatcher;
        _tokenProvider = tokenProvider;
    }

    /// <summary>Produces the HTTP response for one request.</summary>
    public async Task<BridgeHttpResponse> HandleAsync(BridgeHttpRequest request, CancellationToken token)
    {
        if (!string.Equals(request.Path, EndpointPath, StringComparison.Ordinal))
            return BridgeHttpResponse.Text(404, "Not found. The MCP endpoint is " + EndpointPath + ".");
        if (!LoopbackRequestGuard.IsLoopbackHost(request.GetHeader("Host"))
            || !LoopbackRequestGuard.IsAllowedOrigin(request.GetHeader("Origin")))
            return BridgeHttpResponse.Text(403, "Only loopback clients may use the agent bridge.");
        if (!IsAuthorized(request.GetHeader("Authorization")))
            return Unauthorized();
        if (request.Method != "POST")
            return MethodNotAllowed();

        return await HandlePostAsync(request.Body, token);
    }

    private async Task<BridgeHttpResponse> HandlePostAsync(byte[] body, CancellationToken token)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return BridgeHttpResponse.Json(400, JsonRpcMessages.Error(null, JsonRpcErrorCodes.ParseError, "Parse error"));
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
                return await HandleBatchAsync(root, token);

            var reply = await _dispatcher.DispatchAsync(root, token);
            return reply == null ? new BridgeHttpResponse(202) : BridgeHttpResponse.Json(200, reply);
        }
    }

    private async Task<BridgeHttpResponse> HandleBatchAsync(JsonElement batch, CancellationToken token)
    {
        var replies = new List<string>();
        foreach (var message in batch.EnumerateArray())
        {
            var reply = await _dispatcher.DispatchAsync(message, token);
            if (reply != null)
                replies.Add(reply);
        }
        return replies.Count == 0
            ? new BridgeHttpResponse(202)
            : BridgeHttpResponse.Json(200, "[" + string.Join(",", replies) + "]");
    }

    private bool IsAuthorized(string? authorization)
    {
        if (authorization == null || !authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var presented = Encoding.UTF8.GetBytes(authorization[BearerPrefix.Length..].Trim());
        var expected = Encoding.UTF8.GetBytes(_tokenProvider());
        return expected.Length > 0 && CryptographicOperations.FixedTimeEquals(presented, expected);
    }

    private static BridgeHttpResponse Unauthorized()
    {
        var response = BridgeHttpResponse.Text(401,
            "Missing or wrong bearer token. Copy the connect command from Lunima → Settings → Agent bridge (MCP).");
        response.Headers["WWW-Authenticate"] = "Bearer realm=\"lunima\"";
        return response;
    }

    private static BridgeHttpResponse MethodNotAllowed()
    {
        var response = BridgeHttpResponse.Text(405, "Use POST with a JSON-RPC message.");
        response.Headers["Allow"] = "POST";
        return response;
    }
}
