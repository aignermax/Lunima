using System.Text;

namespace CAP.Avalonia.Services.AgentBridge.Transport;

/// <summary>
/// A parsed HTTP/1.1 request as seen by the agent bridge. Header names are
/// case-insensitive; the path excludes any query string.
/// </summary>
public sealed class BridgeHttpRequest
{
    /// <summary>Creates a request.</summary>
    public BridgeHttpRequest(
        string method,
        string path,
        IReadOnlyDictionary<string, string> headers,
        byte[] body,
        bool keepAlive)
    {
        Method = method;
        Path = path;
        Headers = headers;
        Body = body;
        KeepAlive = keepAlive;
    }

    /// <summary>Upper-case HTTP method (e.g. <c>POST</c>).</summary>
    public string Method { get; }

    /// <summary>Request path without the query string (e.g. <c>/mcp</c>).</summary>
    public string Path { get; }

    /// <summary>Request headers keyed case-insensitively.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>Raw request body (empty when none was sent).</summary>
    public byte[] Body { get; }

    /// <summary>Whether the client allows the connection to be reused after the response.</summary>
    public bool KeepAlive { get; }

    /// <summary>Returns the header value or null when the header is absent.</summary>
    public string? GetHeader(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
/// An HTTP response produced by the agent bridge endpoint.
/// </summary>
public sealed class BridgeHttpResponse
{
    private const string JsonContentType = "application/json";

    /// <summary>Creates a response.</summary>
    public BridgeHttpResponse(int statusCode, string? contentType = null, byte[]? body = null)
    {
        StatusCode = statusCode;
        ContentType = contentType;
        Body = body ?? Array.Empty<byte>();
    }

    /// <summary>HTTP status code.</summary>
    public int StatusCode { get; }

    /// <summary>Content type of <see cref="Body"/>, or null for an empty body.</summary>
    public string? ContentType { get; }

    /// <summary>Response body bytes.</summary>
    public byte[] Body { get; }

    /// <summary>Additional response headers (e.g. <c>WWW-Authenticate</c>, <c>Allow</c>).</summary>
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a UTF-8 JSON response.</summary>
    public static BridgeHttpResponse Json(int statusCode, string json) =>
        new(statusCode, JsonContentType, Encoding.UTF8.GetBytes(json));

    /// <summary>Creates a plain-text response (used for transport-level errors).</summary>
    public static BridgeHttpResponse Text(int statusCode, string text) =>
        new(statusCode, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text));
}
