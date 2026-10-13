namespace CAP.Avalonia.Services.AgentBridge.Transport;

/// <summary>
/// DNS-rebinding guard required by the MCP Streamable-HTTP spec for local servers: a web
/// page in the user's browser could otherwise resolve its own domain to 127.0.0.1 and talk
/// to the bridge. Requests must name a loopback host, and a browser <c>Origin</c> (if
/// present) must be a loopback origin as well.
/// </summary>
public static class LoopbackRequestGuard
{
    private static readonly string[] LoopbackHosts = { "127.0.0.1", "localhost", "[::1]", "::1" };

    /// <summary>True when the <c>Host</c> header names a loopback host (any port).</summary>
    public static bool IsLoopbackHost(string? hostHeader)
    {
        if (string.IsNullOrWhiteSpace(hostHeader))
            return false;

        return LoopbackHosts.Contains(StripPort(hostHeader.Trim()), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when no <c>Origin</c> was sent (non-browser clients such as Claude Code) or the
    /// origin is an http(s) loopback origin.
    /// </summary>
    public static bool IsAllowedOrigin(string? originHeader)
    {
        if (string.IsNullOrWhiteSpace(originHeader))
            return true;
        if (!Uri.TryCreate(originHeader.Trim(), UriKind.Absolute, out var origin))
            return false;
        if (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps)
            return false;

        return origin.IsLoopback;
    }

    private static string StripPort(string host)
    {
        if (host.StartsWith('['))
        {
            var close = host.IndexOf(']');
            return close > 0 ? host[..(close + 1)] : host;
        }

        var colon = host.IndexOf(':');
        var lastColon = host.LastIndexOf(':');
        return colon >= 0 && colon == lastColon ? host[..colon] : host;
    }
}
