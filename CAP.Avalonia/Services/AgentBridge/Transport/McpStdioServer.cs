using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using CAP.Avalonia.Services.AgentBridge.Protocol;

namespace CAP.Avalonia.Services.AgentBridge.Transport;

/// <summary>
/// MCP stdio transport: newline-delimited JSON-RPC messages on <c>stdin</c>, one response per
/// line on <c>stdout</c>. Requests run concurrently (the tool catalog still serialises tool
/// execution); <c>notifications/cancelled</c> cancels an in-flight request and suppresses its
/// response, as the MCP specification requires. Returns when the input reaches end of stream
/// and every accepted request has answered.
/// </summary>
public sealed class McpStdioServer
{
    private const string CancelledNotification = "notifications/cancelled";

    private readonly McpJsonRpcDispatcher _dispatcher;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inFlight = new();

    /// <summary>Creates the server over the given streams (production: the process's stdin/stdout).</summary>
    public McpStdioServer(McpJsonRpcDispatcher dispatcher, TextReader input, TextWriter output)
    {
        _dispatcher = dispatcher;
        _input = input;
        _output = output;
    }

    /// <summary>Serves until end of input or <paramref name="token"/> is cancelled.</summary>
    public async Task RunAsync(CancellationToken token)
    {
        var pending = new List<Task>();
        while (!token.IsCancellationRequested)
        {
            var line = await _input.ReadLineAsync(token);
            if (line == null)
                break;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            pending.RemoveAll(t => t.IsCompleted);
            pending.Add(HandleLineAsync(line, token));
        }
        await Task.WhenAll(pending);
    }

    private async Task HandleLineAsync(string line, CancellationToken token)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(line); }
        catch (JsonException)
        {
            await WriteAsync(JsonRpcMessages.Error(null, JsonRpcErrorCodes.ParseError, "Parse error"));
            return;
        }

        using (document)
        {
            var message = document.RootElement;
            if (TryCancel(message))
                return;

            var key = RequestKey(message);
            using var request = CancellationTokenSource.CreateLinkedTokenSource(token);
            if (key != null)
                _inFlight[key] = request;
            try
            {
                var reply = await _dispatcher.DispatchAsync(message, request.Token);
                if (reply != null && !request.IsCancellationRequested)
                    await WriteAsync(reply);
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            catch (Exception ex)
            {
                await WriteAsync(JsonRpcMessages.Error(JsonRpcMessages.ReadId(message),
                    JsonRpcErrorCodes.InternalError, ex.Message));
            }
            finally
            {
                if (key != null)
                    _inFlight.TryRemove(key, out _);
            }
        }
    }

    private bool TryCancel(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("method", out var method)
            || method.ValueKind != JsonValueKind.String
            || method.GetString() != CancelledNotification)
            return false;

        if (message.TryGetProperty("params", out var parameters)
            && parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("requestId", out var requestId)
            && _inFlight.TryGetValue(requestId.GetRawText(), out var source))
        {
            try { source.Cancel(); }
            catch (ObjectDisposedException) { }
        }
        return true;
    }

    private static string? RequestKey(JsonElement message) =>
        message.ValueKind == JsonValueKind.Object && message.TryGetProperty("id", out var id)
            && id.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? id.GetRawText()
            : null;

    private async Task WriteAsync(string reply)
    {
        // A response is one line: compact JSON never contains a raw newline.
        await _writeLock.WaitAsync();
        try
        {
            await _output.WriteAsync(reply);
            await _output.WriteAsync('\n');
            await _output.FlushAsync();
        }
        finally { _writeLock.Release(); }
    }

    /// <summary>Wraps the process's standard streams as UTF-8 without a byte-order mark.</summary>
    public static (TextReader Input, TextWriter Output) OpenStandardStreams()
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var input = new StreamReader(Console.OpenStandardInput(), utf8);
        var output = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = false, NewLine = "\n" };
        return (input, output);
    }
}
