using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CAP_Core.Export.Processes;
using Shouldly;

namespace UnitTests.AgentBridge.Headless.McpProcess;

/// <summary>
/// A minimal MCP client driving a real child process over stdio: newline-delimited JSON-RPC 2.0,
/// one request at a time as Claude Code sends them. Every stdout line is checked against the
/// JSON-RPC framing as soon as it arrives, and stderr is collected for failure diagnostics.
/// </summary>
internal sealed class McpStdioProcessClient : IAsyncDisposable
{
    // Well below the CI hang guard (--blame-hang-timeout 2m), so a hang fails here with stderr attached.
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PumpShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly OwnedProcess _process;
    private readonly StringBuilder _standardError = new();
    private readonly Task _standardErrorPump;
    private readonly List<string> _stdoutLines = [];
    private int _nextId;

    private McpStdioProcessClient(OwnedProcess process)
    {
        _process = process;
        _standardErrorPump = PumpStandardErrorAsync();
    }

    /// <summary>Starts <paramref name="startInfo"/> as an owned process (its own Job / process group).</summary>
    public static McpStdioProcessClient Start(ProcessStartInfo startInfo) => new(OwnedProcess.Start(startInfo));

    /// <summary>The server process identifier.</summary>
    public int ProcessId => _process.Id;

    /// <summary>Every line the server wrote to stdout so far.</summary>
    public IReadOnlyList<string> StdoutLines => _stdoutLines;

    /// <summary>Everything the server wrote to stderr so far.</summary>
    public string StandardErrorText
    {
        get { lock (_standardError) return _standardError.ToString(); }
    }

    /// <summary>Sends a request and returns its <c>result</c>; a JSON-RPC error fails the test.</summary>
    public async Task<JsonElement> RequestAsync(string method, object parameters)
    {
        var id = ++_nextId;
        await SendAsync(new { jsonrpc = "2.0", id, method, @params = parameters });
        while (true)
        {
            var line = await ReadLineAsync($"the response to '{method}'")
                ?? throw new ShouldAssertException($"stdout closed before the response to '{method}'.\nstderr:\n{StandardErrorText}");
            using var message = JsonDocument.Parse(line);
            var root = message.RootElement;
            if (!AnswersRequest(root, id))
                continue;
            root.TryGetProperty("error", out var error).ShouldBeFalse($"'{method}' failed: {error}");
            return root.GetProperty("result").Clone();
        }
    }

    /// <summary>Calls a tool and returns its JSON answer; a tool error fails the test.</summary>
    public async Task<JsonElement> CallToolAsync(string name, object arguments)
    {
        var result = await RequestAsync("tools/call", new { name, arguments });
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        result.GetProperty("isError").GetBoolean().ShouldBeFalse($"{name} reported an error: {text}");
        using var answer = JsonDocument.Parse(text);
        return answer.RootElement.Clone();
    }

    /// <summary>Sends a notification (no response expected).</summary>
    public Task NotifyAsync(string method) => SendAsync(new { jsonrpc = "2.0", method });

    /// <summary>
    /// Closes stdin — how an MCP client ends the session — and returns the exit code; not exiting
    /// within <paramref name="deadline"/> fails the test.
    /// </summary>
    public async Task<int> CloseInputAndWaitForExitAsync(TimeSpan deadline)
    {
        _process.StandardInput.Close();
        try { await _process.WaitForExitAsync().WaitAsync(deadline); }
        catch (TimeoutException)
        {
            throw new ShouldAssertException($"The server was still running {deadline} after stdin closed.\nstderr:\n{StandardErrorText}");
        }
        return _process.ExitCode;
    }

    /// <summary>Reads stdout to its end, so trailing output after the last response is checked too.</summary>
    public async Task DrainStandardOutputAsync()
    {
        while (await ReadLineAsync("the end of stdout") != null) { }
    }

    /// <summary>Retires the process scope, then lets the stderr reader finish.</summary>
    public async ValueTask DisposeAsync()
    {
        await _process.DisposeAsync();
        try { await _standardErrorPump.WaitAsync(PumpShutdownTimeout); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or TimeoutException) { }
    }

    /// <summary>A response to request <paramref name="id"/>, not a server-initiated request reusing the number.</summary>
    private static bool AnswersRequest(JsonElement message, int id) =>
        !message.TryGetProperty("method", out _)
        && message.TryGetProperty("id", out var answered)
        && answered.ValueKind == JsonValueKind.Number
        && answered.TryGetInt32(out var number) && number == id;

    private Task SendAsync(object message) =>
        _process.StandardInput.WriteAsync(JsonSerializer.Serialize(message) + "\n");

    private async Task<string?> ReadLineAsync(string awaited)
    {
        string? line;
        try { line = await _process.StandardOutput.ReadLineAsync().WaitAsync(ResponseTimeout); }
        catch (TimeoutException)
        {
            throw new ShouldAssertException($"No stdout line within {ResponseTimeout} while awaiting {awaited}.\nstderr:\n{StandardErrorText}");
        }
        if (line == null)
            return null;
        _stdoutLines.Add(line);
        JsonRpcLine.Violation(line).ShouldBeNull($"stdout must carry only JSON-RPC messages, but got: {line}");
        return line;
    }

    private async Task PumpStandardErrorAsync()
    {
        while (await _process.StandardError.ReadLineAsync() is { } line)
        {
            lock (_standardError) _standardError.AppendLine(line);
        }
    }
}
