using System.Globalization;
using System.Text;

namespace CAP.Avalonia.Services.AgentBridge.Transport;

/// <summary>
/// Serializes a <see cref="BridgeHttpResponse"/> as an HTTP/1.1 response with an explicit
/// <c>Content-Length</c> (no chunking, no streaming — the bridge answers every MCP request
/// with a single JSON body).
/// </summary>
public static class BridgeHttpResponseWriter
{
    /// <summary>Writes <paramref name="response"/> to <paramref name="stream"/> and flushes.</summary>
    public static async Task WriteAsync(
        Stream stream, BridgeHttpResponse response, bool keepAlive, CancellationToken token)
    {
        var head = new StringBuilder();
        head.Append(CultureInfo.InvariantCulture,
            $"HTTP/1.1 {response.StatusCode} {ReasonPhrase(response.StatusCode)}\r\n");
        if (response.ContentType != null)
            head.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
        head.Append(CultureInfo.InvariantCulture, $"Content-Length: {response.Body.Length}\r\n");
        head.Append("Cache-Control: no-store\r\n");
        head.Append("Connection: ").Append(keepAlive ? "keep-alive" : "close").Append("\r\n");
        foreach (var (name, value) in response.Headers)
            head.Append(name).Append(": ").Append(value).Append("\r\n");
        head.Append("\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), token);
        if (response.Body.Length > 0)
            await stream.WriteAsync(response.Body, token);
        await stream.FlushAsync(token);
    }

    /// <summary>Standard reason phrase for the status codes the bridge emits.</summary>
    public static string ReasonPhrase(int statusCode) => statusCode switch
    {
        200 => "OK",
        202 => "Accepted",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        413 => "Payload Too Large",
        415 => "Unsupported Media Type",
        431 => "Request Header Fields Too Large",
        500 => "Internal Server Error",
        _ => "Status",
    };
}
