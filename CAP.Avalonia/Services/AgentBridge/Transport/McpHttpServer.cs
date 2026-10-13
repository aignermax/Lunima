using System.Net;
using System.Net.Sockets;

namespace CAP.Avalonia.Services.AgentBridge.Transport;

/// <summary>
/// Loopback-only HTTP/1.1 server for the MCP agent bridge. Binds a raw socket to
/// <see cref="IPAddress.Loopback"/> (127.0.0.1), so it never accepts connections from
/// another machine, needs no admin rights or URL ACL on Windows, and behaves identically on
/// macOS and Linux. Each accepted connection is served on the thread pool; the HTTP
/// semantics live in <see cref="McpHttpEndpoint"/>.
/// </summary>
public sealed class McpHttpServer : IDisposable, IAsyncDisposable
{
    /// <summary>A kept-alive connection idle longer than this is closed.</summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);

    private const int StreamBufferBytes = 8 * 1024;

    private readonly McpHttpEndpoint _endpoint;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    /// <summary>Creates a server that dispatches requests to <paramref name="endpoint"/>.</summary>
    public McpHttpServer(McpHttpEndpoint endpoint) => _endpoint = endpoint;

    /// <summary>Whether the listener is currently accepting connections.</summary>
    public bool IsRunning => _listener != null;

    /// <summary>The bound port (useful when started with port 0); 0 when stopped.</summary>
    public int Port { get; private set; }

    /// <summary>
    /// Starts listening on 127.0.0.1:<paramref name="port"/> (0 picks an ephemeral port).
    /// </summary>
    /// <exception cref="SocketException">The port is in use or cannot be bound.</exception>
    public void Start(int port)
    {
        if (_listener != null)
            throw new InvalidOperationException("The agent bridge server is already running.");

        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
    }

    /// <summary>Stops listening and waits for the accept loop to finish.</summary>
    public async Task StopAsync()
    {
        if (_listener == null)
            return;

        _cts?.Cancel();
        _listener.Stop();
        _listener = null;
        Port = 0;
        if (_acceptLoop != null)
            await _acceptLoop.ConfigureAwait(false);
        _cts?.Dispose();
        _cts = null;
        _acceptLoop = null;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() => await StopAsync();

    /// <summary>
    /// Stops listening without waiting for the accept loop (synchronous container disposal
    /// at application exit); the loop ends on its own once the listener is closed.
    /// </summary>
    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener = null;
        Port = 0;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
            _ = Task.Run(() => ServeConnectionAsync(client, token), CancellationToken.None);
        }
    }

    private async Task ServeConnectionAsync(TcpClient client, CancellationToken serverToken)
    {
        using (client)
        await using (var stream = new BufferedStream(client.GetStream(), StreamBufferBytes))
        {
            try
            {
                await ServeRequestsAsync(stream, serverToken);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // The client went away or the server stopped — nothing to answer.
            }
        }
    }

    private async Task ServeRequestsAsync(Stream stream, CancellationToken serverToken)
    {
        while (!serverToken.IsCancellationRequested)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
            idle.CancelAfter(IdleTimeout);

            BridgeHttpRequest? request;
            try
            {
                request = await BridgeHttpRequestReader.ReadAsync(stream, idle.Token);
            }
            catch (BridgeHttpException ex)
            {
                await BridgeHttpResponseWriter.WriteAsync(stream,
                    BridgeHttpResponse.Text(ex.StatusCode, ex.Message), keepAlive: false, serverToken);
                return;
            }
            if (request == null)
                return;

            var response = await HandleSafelyAsync(request, serverToken);
            await BridgeHttpResponseWriter.WriteAsync(stream, response, request.KeepAlive, serverToken);
            if (!request.KeepAlive)
                return;
        }
    }

    /// <summary>
    /// Runs the endpoint; an unexpected failure becomes a 500 with the reason instead of a
    /// silently dropped connection the agent would only see as a timeout.
    /// </summary>
    private async Task<BridgeHttpResponse> HandleSafelyAsync(BridgeHttpRequest request, CancellationToken token)
    {
        try
        {
            return await _endpoint.HandleAsync(request, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return BridgeHttpResponse.Text(500, "Agent bridge error: " + ex.Message);
        }
    }
}
