using System.Globalization;
using System.Net;
using System.Text;
using CAP_Core;

namespace CAP.Avalonia.Services.LiveState;

/// <summary>
/// Minimal localhost-only HTTP/JSON server that exposes <see cref="LiveStateApi"/>
/// to external agents (via the mcp-servers/livestate stdio bridge). Bound to
/// 127.0.0.1 exclusively — never reachable from the network.
/// </summary>
public class LiveStateHttpServer : IDisposable
{
    /// <summary>Default TCP port when none is configured.</summary>
    public const int DefaultPort = 5252;

    /// <summary>Environment variable overriding the port.</summary>
    public const string PortEnvironmentVariable = "LUNIMA_AGENT_PORT";

    private readonly LiveStateApi _api;
    private readonly ErrorConsoleService _errorConsole;
    private HttpListener? _listener;

    /// <summary>True while the server accepts requests.</summary>
    public bool IsRunning => _listener?.IsListening == true;

    /// <summary>Port the server is (or will be) listening on.</summary>
    public int Port { get; private set; } = ResolveConfiguredPort();

    /// <summary>Base URL of the running server.</summary>
    public string BaseUrl => FormattableString.Invariant($"http://127.0.0.1:{Port}");

    /// <summary>Initializes the server around the given API.</summary>
    public LiveStateHttpServer(LiveStateApi api, ErrorConsoleService errorConsole)
    {
        _api = api;
        _errorConsole = errorConsole;
    }

    /// <summary>Reads the port from <see cref="PortEnvironmentVariable"/>, falling back to the default.</summary>
    public static int ResolveConfiguredPort()
    {
        var raw = Environment.GetEnvironmentVariable(PortEnvironmentVariable);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)
            && port is > 0 and < 65536 ? port : DefaultPort;
    }

    /// <summary>
    /// Starts listening on 127.0.0.1 at the given (or configured) port.
    /// Returns false when the port is already taken.
    /// </summary>
    public bool Start(int? port = null)
    {
        if (IsRunning) return true;
        Port = port ?? Port;

        var listener = new HttpListener();
        listener.Prefixes.Add(FormattableString.Invariant($"http://127.0.0.1:{Port}/"));
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            _errorConsole.LogError($"Agent server could not bind port {Port}", ex);
            return false;
        }

        _listener = listener;
        _ = AcceptLoopAsync(listener);
        _errorConsole.LogInfo($"Agent server listening on {BaseUrl}");
        return true;
    }

    /// <summary>Stops the server; in-flight requests are aborted.</summary>
    public void Stop()
    {
        var listener = _listener;
        _listener = null;
        if (listener == null) return;
        try { listener.Stop(); listener.Close(); }
        catch (ObjectDisposedException) { /* already torn down */ }
        _errorConsole.LogInfo("Agent server stopped");
    }

    private async Task AcceptLoopAsync(HttpListener listener)
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception) when (!listener.IsListening)
            {
                return; // Stop() disposed the listener — normal shutdown.
            }
            _ = HandleRequestAsync(context);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            string body;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                body = await reader.ReadToEndAsync();

            var (statusCode, json) = await _api.HandleAsync(
                context.Request.HttpMethod, context.Request.Url?.AbsolutePath ?? "/", body);

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
        }
        catch (Exception ex)
        {
            _errorConsole.LogError("Agent server request failed", ex);
        }
        finally
        {
            try { context.Response.Close(); } catch (ObjectDisposedException) { /* client gone */ }
        }
    }

    /// <inheritdoc />
    public void Dispose() => Stop();
}
