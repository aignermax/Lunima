using System.Globalization;
using System.Text;

namespace CAP.Avalonia.Services.AgentBridge.Transport;

/// <summary>
/// Minimal HTTP/1.1 request parser for the loopback agent bridge: request line, headers,
/// and a body framed by <c>Content-Length</c> or <c>Transfer-Encoding: chunked</c>.
/// Enforces size limits so a misbehaving local client cannot exhaust memory.
/// </summary>
public static class BridgeHttpRequestReader
{
    /// <summary>Largest accepted request line or header line, in bytes.</summary>
    public const int MaxLineBytes = 16 * 1024;

    /// <summary>Largest accepted number of header lines.</summary>
    public const int MaxHeaderCount = 100;

    /// <summary>Largest accepted request body, in bytes.</summary>
    public const int MaxBodyBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Reads one request from <paramref name="stream"/>. Returns null when the peer closed
    /// the connection before sending a request line.
    /// </summary>
    /// <exception cref="BridgeHttpException">The request is malformed or exceeds a limit.</exception>
    public static async Task<BridgeHttpRequest?> ReadAsync(Stream stream, CancellationToken token)
    {
        var requestLine = await ReadLineAsync(stream, token);
        if (requestLine == null)
            return null;

        var parts = requestLine.Split(' ');
        if (parts.Length != 3 || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
            throw new BridgeHttpException(400, "Malformed request line.");

        var headers = await ReadHeadersAsync(stream, token);
        var body = await ReadBodyAsync(stream, headers, token);
        var path = parts[1].Split('?', 2)[0];
        return new BridgeHttpRequest(parts[0].ToUpperInvariant(), path, headers, body,
            IsKeepAlive(parts[2], headers));
    }

    private static bool IsKeepAlive(string version, IReadOnlyDictionary<string, string> headers)
    {
        headers.TryGetValue("Connection", out var connection);
        if (version == "HTTP/1.0")
            return string.Equals(connection, "keep-alive", StringComparison.OrdinalIgnoreCase);
        return !string.Equals(connection, "close", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Dictionary<string, string>> ReadHeadersAsync(Stream stream, CancellationToken token)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int count = 0; ; count++)
        {
            if (count > MaxHeaderCount)
                throw new BridgeHttpException(431, "Too many headers.");
            var line = await ReadLineAsync(stream, token)
                ?? throw new BridgeHttpException(400, "Connection closed inside the header block.");
            if (line.Length == 0)
                return headers;

            var colon = line.IndexOf(':');
            if (colon <= 0)
                throw new BridgeHttpException(400, "Malformed header line.");
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            headers[name] = headers.TryGetValue(name, out var existing) ? $"{existing}, {value}" : value;
        }
    }

    private static async Task<byte[]> ReadBodyAsync(
        Stream stream, IReadOnlyDictionary<string, string> headers, CancellationToken token)
    {
        if (headers.TryGetValue("Transfer-Encoding", out var encoding)
            && encoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
            return await ReadChunkedBodyAsync(stream, token);

        if (!headers.TryGetValue("Content-Length", out var lengthText))
            return Array.Empty<byte>();
        if (!int.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out var length))
            throw new BridgeHttpException(400, "Invalid Content-Length.");
        if (length > MaxBodyBytes)
            throw new BridgeHttpException(413, "Request body too large.");
        return await ReadExactlyAsync(stream, length, token);
    }

    private static async Task<byte[]> ReadChunkedBodyAsync(Stream stream, CancellationToken token)
    {
        using var body = new MemoryStream();
        while (true)
        {
            var sizeLine = await ReadLineAsync(stream, token)
                ?? throw new BridgeHttpException(400, "Connection closed inside a chunked body.");
            var sizeText = sizeLine.Split(';', 2)[0].Trim();
            if (!int.TryParse(sizeText, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var size)
                || size < 0)
                throw new BridgeHttpException(400, "Invalid chunk size.");
            if (size == 0)
            {
                await ReadHeadersAsync(stream, token);
                return body.ToArray();
            }
            if (body.Length + size > MaxBodyBytes)
                throw new BridgeHttpException(413, "Request body too large.");
            body.Write(await ReadExactlyAsync(stream, size, token));
            await ReadLineAsync(stream, token);
        }
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int length, CancellationToken token)
    {
        var buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer, token);
        return buffer;
    }

    /// <summary>Reads one CRLF- (or LF-) terminated ASCII line; null on clean EOF before any byte.</summary>
    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken token)
    {
        var bytes = new List<byte>();
        var single = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(single, token);
            if (read == 0)
                return bytes.Count == 0 ? null : throw new BridgeHttpException(400, "Unexpected end of stream.");
            if (single[0] == (byte)'\n')
                break;
            if (bytes.Count >= MaxLineBytes)
                throw new BridgeHttpException(431, "Header line too long.");
            bytes.Add(single[0]);
        }
        if (bytes.Count > 0 && bytes[^1] == (byte)'\r')
            bytes.RemoveAt(bytes.Count - 1);
        return Encoding.ASCII.GetString(bytes.ToArray());
    }
}

/// <summary>A transport-level HTTP error carrying the status code to answer with.</summary>
public sealed class BridgeHttpException : Exception
{
    /// <summary>Creates the exception.</summary>
    public BridgeHttpException(int statusCode, string message) : base(message) => StatusCode = statusCode;

    /// <summary>HTTP status code describing the failure.</summary>
    public int StatusCode { get; }
}
