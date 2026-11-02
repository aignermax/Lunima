using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CAP.Avalonia.Services.AgentBridge.Transport;

namespace CAP.Avalonia.Services.AgentBridge.Headless;

/// <summary>
/// Executes a parsed <see cref="AgentCliCommand"/> against a <see cref="HeadlessAgentSession"/>.
/// Runs on the Avalonia UI thread; the stdio MCP server reads its input on a worker thread.
/// </summary>
public sealed class AgentCliRunner
{
    /// <summary>Exit code: everything succeeded.</summary>
    public const int Success = 0;

    /// <summary>Exit code: a tool reported an error (or the design could not be opened/saved).</summary>
    public const int ToolFailed = 1;

    /// <summary>Exit code: the command line or script was malformed.</summary>
    public const int UsageError = 2;

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly HeadlessAgentSession _session;
    private readonly TextReader _input;
    private readonly TextWriter _output;

    /// <summary>Creates the runner; <paramref name="output"/> receives only results (stdout).</summary>
    public AgentCliRunner(HeadlessAgentSession session, TextReader input, TextWriter output)
    {
        _session = session;
        _input = input;
        _output = output;
    }

    /// <summary>Runs <paramref name="command"/> and returns the process exit code.</summary>
    public async Task<int> RunAsync(AgentCliCommand command, CancellationToken token)
    {
        if (command.DesignPath != null)
            await _session.OpenDesignAsync(command.DesignPath);

        var exitCode = command.Verb switch
        {
            AgentCliVerb.Mcp => await ServeMcpAsync(token),
            AgentCliVerb.Tools => await WriteToolsAsync(),
            AgentCliVerb.Call => await CallAsync(command),
            AgentCliVerb.Run => await RunScriptAsync(command),
            _ => UsageError,
        };

        if (exitCode == Success && command.SavePath != null)
            await _session.SaveDesignAsync(command.SavePath.Length > 0 ? command.SavePath : command.DesignPath!);
        return exitCode;
    }

    private async Task<int> ServeMcpAsync(CancellationToken token)
    {
        var server = new McpStdioServer(_session.Dispatcher, _input, _output);
        // Console input blocks; read it off the UI thread so tool calls can run meanwhile.
        await Task.Run(() => server.RunAsync(token), token);
        return Success;
    }

    private async Task<int> WriteToolsAsync()
    {
        var tools = new JsonArray();
        foreach (var tool in _session.Catalog.Tools)
        {
            tools.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["input_schema"] = JsonSerializer.SerializeToNode(tool.InputSchema),
            });
        }
        await WriteAsync(tools);
        return Success;
    }

    private async Task<int> CallAsync(AgentCliCommand command)
    {
        if (!TryParseArguments(command.ArgumentsJson, out var arguments, out var error))
            return await UsageAsync(error!);
        if (!_session.Catalog.Contains(command.ToolName!))
            return await UsageAsync($"Unknown tool '{command.ToolName}'. List them with: lunima agent tools");

        var writer = new AgentCliResultWriter(command.ImageDirectory);
        var result = await _session.Catalog.InvokeAsync(command.ToolName!, arguments);
        await WriteAsync(writer.Describe(command.ToolName!, result));
        return result.IsError ? ToolFailed : Success;
    }

    private async Task<int> RunScriptAsync(AgentCliCommand command)
    {
        var text = command.ScriptPath == "-" ? await _input.ReadToEndAsync() : await File.ReadAllTextAsync(command.ScriptPath!);
        if (!AgentScript.TryParse(text, out var steps, out var error))
            return await UsageAsync(error!);
        foreach (var step in steps.Where(s => !_session.Catalog.Contains(s.Tool)))
            return await UsageAsync($"Unknown tool '{step.Tool}' in the script.");

        var writer = new AgentCliResultWriter(command.ImageDirectory);
        var outputs = new JsonArray();
        var exitCode = Success;
        foreach (var step in steps)
        {
            var result = await _session.Catalog.InvokeAsync(step.Tool, step.Arguments);
            outputs.Add(writer.Describe(step.Tool, result));
            if (!result.IsError) continue;
            exitCode = ToolFailed;
            if (!command.KeepGoing) break;
        }
        await WriteAsync(outputs);
        return exitCode;
    }

    private static bool TryParseArguments(string json, out JsonElement arguments, out string? error)
    {
        arguments = default;
        error = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Tool arguments must be a JSON object, e.g. '{\"blocked_only\": true}'.";
                return false;
            }
            arguments = document.RootElement.Clone();
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Tool arguments are not valid JSON: {ex.Message}";
            return false;
        }
    }

    private async Task<int> UsageAsync(string message)
    {
        await Console.Error.WriteLineAsync(message);
        return UsageError;
    }

    private async Task WriteAsync(JsonNode node)
    {
        await _output.WriteLineAsync(node.ToJsonString(Indented));
        await _output.FlushAsync();
    }
}
