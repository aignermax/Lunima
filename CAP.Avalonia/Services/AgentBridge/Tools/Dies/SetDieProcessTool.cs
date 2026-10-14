using System.Text.Json;
using CAP.Avalonia.Commands.Dies;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Dies;

/// <summary>
/// <c>set_die_process</c>: sets a die's process and/or its enabled PDK subset (the parts the
/// library offers on that die), undoable like the die properties panel.
/// </summary>
public sealed class SetDieProcessTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public SetDieProcessTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "set_die_process";

    /// <inheritdoc/>
    public string Description =>
        "Set a die's fabrication process (display name or PDK name) and/or the PDKs enabled on it " +
        "(subset of the process's PDKs; empty or omitted = all). Undoable.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            die = new { type = "string", description = "Die name from list_dies." },
            process = new { type = "string", description = "New process (display name or PDK name); omit to keep." },
            pdks = new { type = "array", items = new { type = "string" }, description = "PDKs enabled on the die." },
        },
        required = new[] { "die" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var die = DieToolSupport.FindDie(_session, ToolArguments.GetString(arguments, "die"));
        if (die == null)
            return Task.FromResult(AgentToolResult.Error("No such die. Use list_dies."));

        var state = DieState.Of(die);
        var processName = ToolArguments.GetString(arguments, "process");
        var process = processName == null ? state.Process : DieToolSupport.FindProcess(_session, processName);
        if (processName != null && process == null)
            return Task.FromResult(AgentToolResult.Error(
                $"Unknown process '{processName}'. Known: {DieToolSupport.ProcessNames(_session)}."));

        var pdks = ToolArguments.GetStringList(arguments, "pdks");
        var unknown = pdks.Where(p => process?.MemberPdkNames.Contains(p, StringComparer.OrdinalIgnoreCase) != true).ToList();
        if (unknown.Count > 0)
            return Task.FromResult(AgentToolResult.Error(
                $"PDK(s) {string.Join(", ", unknown)} do not belong to the die's process '{process?.DisplayName}'."));

        var after = state with
        {
            Process = process,
            Definition = state.Definition with { EnabledPdkNames = pdks.Count > 0 ? pdks.ToList() : null },
        };
        var command = UpdateDieCommand.TryCreate(_session.Canvas, die, after, out var refusal);
        if (command != null)
            _session.Commands.ExecuteCommand(command);
        else if (refusal != null)
            return Task.FromResult(AgentToolResult.Error(refusal));
        return Task.FromResult(AgentToolResult.Json(DieToolSupport.Describe(die)));
    }
}
