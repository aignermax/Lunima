using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using CAP.Avalonia.Services.AgentBridge.Hosting;
using CAP.Avalonia.Services.AgentBridge.Protocol;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AiTools;

namespace UnitTests.AgentBridge;

/// <summary>Runs dispatched work inline on the calling thread (no Avalonia dispatcher in plain unit tests).</summary>
internal sealed class InlineUiDispatcher : IUiDispatcher
{
    public Task<T> InvokeAsync<T>(Func<Task<T>> work) => work();
}

/// <summary>Bridge tool that echoes its <c>text</c> argument, or throws when asked to.</summary>
internal sealed class EchoBridgeTool : IAgentBridgeTool
{
    public string Name { get; init; } = "echo";
    public string Description => "Echo the text argument.";
    public object InputSchema => new { type = "object", properties = new { text = new { type = "string" } } };

    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        if (arguments.TryGetProperty("throw", out _))
            throw new InvalidOperationException("boom");
        var text = arguments.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        return Task.FromResult(AgentToolResult.Text(text));
    }
}

/// <summary>In-app assistant tool returning a fixed string.</summary>
internal sealed class FixedAiTool : IAiTool
{
    public FixedAiTool(string name, string result)
    {
        Name = name;
        Result = result;
    }

    public string Name { get; }
    public string Result { get; }
    public string Description => "Assistant tool " + Name;
    public object InputSchema => new { type = "object", properties = new { } };
    public Task<string> ExecuteAsync(JsonElement input) => Task.FromResult(Result);
}

/// <summary>Builds the protocol stack over stub tools.</summary>
internal static class AgentBridgeTestStack
{
    public const string Token = "test-token-123";

    public static AgentToolCatalog Catalog(params IAiTool[] assistantTools) =>
        new(new IAgentBridgeTool[] { new EchoBridgeTool() },
            new AiToolRegistry(assistantTools),
            new InlineUiDispatcher());

    public static McpJsonRpcDispatcher Dispatcher(params IAiTool[] assistantTools) =>
        new(Catalog(assistantTools), "1.2.3");

    public static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static IReadOnlyDictionary<string, string> Headers(params (string Name, string Value)[] headers)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in headers)
            dict[name] = value;
        return dict;
    }
}
