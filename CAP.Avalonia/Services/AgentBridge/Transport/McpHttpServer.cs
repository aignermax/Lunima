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
    private readonly Action<Exception>? _reportCancellationError;
    private readonly object _lifecycle = new();
    private McpServerSession? _session;
    private Task _stopping = Task.CompletedTask;

    /// <summary>Creates a server that dispatches requests to <paramref name="endpoint"/>.</summary>
    public McpHttpServer(McpHttpEndpoint endpoint, Action<Exception>? reportCancellationError = null)
    {
        _endpoint = endpoint;
        _reportCancellationError = reportCancellationError;
    }

    /// <summary>Callback diagnostics from the latest shutdown; they do not prevent restart.</summary>
    public Exception? LastCancellationError { get; private set; }

    /// <summary>Whether the listener is currently accepting connections.</summary>
    public bool IsRunning { get { lock (_lifecycle) return _session != null; } }

    /// <summary>The bound port (useful when started with port 0); 0 when stopped.</summary>
    public int Port { get; private set; }

    /// <summary>
    /// Starts listening on 127.0.0.1:<paramref name="port"/> (0 picks an ephemeral port).
    /// </summary>
    /// <exception cref="SocketException">The port is in use or cannot be bound.</exception>
    public void Start(int port)
    {
        lock (_lifecycle)
        {
            if (_session != null || !_stopping.IsCompleted)
                throw new InvalidOperationException("The agent bridge server is already running or stopping.");
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            try { _endpoint.OpenAdmission(); }
            catch { listener.Stop(); throw; }
            var session = new McpServerSession(listener);
            _session = session;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            session.AcceptLoop = Task.Run(() => AcceptLoopAsync(session));
        }
    }

    /// <summary>Revokes admission, stops listening, and drains every accepted connection and tool.</summary>
    public Task StopAsync()
    {
        McpServerSession session;
        TaskCompletionSource completion;
        lock (_lifecycle)
        {
            if (_session == null) return _stopping;
            session = _session;
            _session = null;
            Port = 0;
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _stopping = completion.Task;
        }
        var tools = _endpoint.CloseAdmissionAsync();
        session.Listener.Stop();
        _ = DrainAsync(session, tools, completion);
        return completion.Task;
    }

    private async Task DrainAsync(McpServerSession session, Task tools, TaskCompletionSource completion)
    {
        try
        {
            AggregateException? transportError = null;
            using (session)
            {
                try { await session.Cancellation.CancelAsync().ConfigureAwait(false); }
                catch (AggregateException error) { transportError = error; }
                finally { await Task.WhenAll(session.DrainAsync(), tools).ConfigureAwait(false); }
            }
            LastCancellationError = transportError == null ? _endpoint.CancellationError
                : _endpoint.CancellationError is { } toolError
                    ? new AggregateException(transportError, toolError) : transportError;
            if (LastCancellationError != null) ReportCancellationError(LastCancellationError);
            completion.SetResult();
        }
        catch (Exception error) { completion.SetException(error); }
    }

    private void ReportCancellationError(Exception error)
    {
        try
        {
            if (_reportCancellationError != null) _reportCancellationError(error);
            else System.Diagnostics.Trace.TraceError("Agent bridge cancellation callback failed: {0}", error);
        }
        catch { /* A diagnostic sink cannot invalidate a completed shutdown. */ }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() => await StopAsync();

    /// <summary>
    /// Revokes admission immediately and starts asynchronous draining during container disposal.
    /// </summary>
    public void Dispose()
    {
        _ = StopAsync();
    }

    private async Task AcceptLoopAsync(McpServerSession session)
    {
        var token = session.Cancellation.Token;
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await session.Listener.AcceptTcpClientAsync(token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
            session.Track(() => ServeConnectionAsync(client, token));
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
