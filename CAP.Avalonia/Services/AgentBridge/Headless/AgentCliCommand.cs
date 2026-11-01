namespace CAP.Avalonia.Services.AgentBridge.Headless;

/// <summary>What a headless invocation does.</summary>
public enum AgentCliVerb
{
    /// <summary>Serve MCP over stdio until the client closes the input.</summary>
    Mcp,

    /// <summary>Print every tool with its description and argument schema.</summary>
    Tools,

    /// <summary>Run one tool.</summary>
    Call,

    /// <summary>Run a JSON script: an array of <c>{"tool": …, "arguments": {…}}</c> steps.</summary>
    Run,

    /// <summary>Print usage.</summary>
    Help,
}

/// <summary>
/// A parsed headless command line:
/// <code>
/// lunima mcp [--design FILE]
/// lunima agent tools
/// lunima agent call TOOL [JSON] [--design FILE] [--save [FILE]] [--images DIR]
/// lunima agent run SCRIPT|- [--design FILE] [--save [FILE]] [--images DIR] [--keep-going]
/// </code>
/// </summary>
/// <param name="Verb">The action.</param>
/// <param name="ToolName">Tool for <see cref="AgentCliVerb.Call"/>.</param>
/// <param name="ArgumentsJson">Tool arguments as a JSON object (default <c>{}</c>).</param>
/// <param name="ScriptPath">Script file for <see cref="AgentCliVerb.Run"/>; <c>-</c> reads stdin.</param>
/// <param name="DesignPath">Design opened before the first tool runs.</param>
/// <param name="SavePath">Where to save afterwards: empty means back to <paramref name="DesignPath"/>, null means no save.</param>
/// <param name="ImageDirectory">Where image results (screenshots) are written; a temp folder when null.</param>
/// <param name="KeepGoing">Run the remaining script steps after a failed one.</param>
public sealed record AgentCliCommand(
    AgentCliVerb Verb,
    string? ToolName = null,
    string ArgumentsJson = "{}",
    string? ScriptPath = null,
    string? DesignPath = null,
    string? SavePath = null,
    string? ImageDirectory = null,
    bool KeepGoing = false)
{
    /// <summary>First argument that starts the MCP stdio server.</summary>
    public const string McpVerb = "mcp";

    /// <summary>First argument that starts a one-shot agent command.</summary>
    public const string AgentVerb = "agent";

    /// <summary>Usage text printed for <c>--help</c> and on a malformed command line.</summary>
    public const string Usage =
        "Lunima headless agent interface\n\n" +
        "  lunima mcp [--design FILE]\n" +
        "      MCP server on stdin/stdout (for Claude Code: claude mcp add lunima -- <path-to-lunima> mcp)\n" +
        "  lunima agent tools\n" +
        "      List every tool with its argument schema (JSON).\n" +
        "  lunima agent call TOOL [JSON-ARGS] [--design FILE] [--save [FILE]] [--images DIR]\n" +
        "      Run one tool, print its result as JSON.\n" +
        "  lunima agent run SCRIPT|- [--design FILE] [--save [FILE]] [--images DIR] [--keep-going]\n" +
        "      Run [{\"tool\": \"...\", \"arguments\": {...}}, ...] from a file or stdin.\n\n" +
        "--design opens a .lun design first; --save writes it back afterwards (to FILE, or to the\n" +
        "--design file). Exit code: 0 success, 1 a tool reported an error, 2 bad command line.";

    /// <summary>Whether <paramref name="args"/> selects the headless agent interface.</summary>
    public static bool IsAgentCommand(IReadOnlyList<string> args) =>
        args.Count > 0 && args[0] is McpVerb or AgentVerb;

    /// <summary>Parses <paramref name="args"/>; returns null and an error for a malformed line.</summary>
    public static AgentCliCommand? Parse(IReadOnlyList<string> args, out string? error)
    {
        error = null;
        if (!IsAgentCommand(args))
        {
            error = "Expected 'mcp' or 'agent'.";
            return null;
        }

        var positional = new List<string>();
        var options = new AgentCliOptions();
        if (!options.TryRead(args.Skip(1).ToList(), positional, out error))
            return null;

        if (args[0] == McpVerb)
            return positional.Count == 0
                ? options.Apply(new AgentCliCommand(AgentCliVerb.Mcp))
                : Fail($"Unexpected argument '{positional[0]}'.", out error);

        return ParseAgent(positional, options, out error);
    }

    private static AgentCliCommand? ParseAgent(List<string> positional, AgentCliOptions options, out string? error)
    {
        error = null;
        var verb = positional.Count == 0 ? "help" : positional[0];
        switch (verb)
        {
            case "tools" when positional.Count == 1:
                return options.Apply(new AgentCliCommand(AgentCliVerb.Tools));
            case "call" when positional.Count is 2 or 3:
                return options.Apply(new AgentCliCommand(AgentCliVerb.Call, positional[1],
                    positional.Count == 3 ? positional[2] : "{}"));
            case "run" when positional.Count == 2:
                return options.Apply(new AgentCliCommand(AgentCliVerb.Run, ScriptPath: positional[1]));
            case "help" or "--help" or "-h":
                return new AgentCliCommand(AgentCliVerb.Help);
            default:
                return Fail($"Unknown or incomplete agent command '{string.Join(' ', positional)}'.", out error);
        }
    }

    private static AgentCliCommand? Fail(string message, out string? error)
    {
        error = message;
        return null;
    }
}
