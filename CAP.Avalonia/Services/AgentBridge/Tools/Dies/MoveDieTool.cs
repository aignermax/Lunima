using System.Text.Json;
using CAP.Avalonia.Commands;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Dies;

/// <summary>
/// <c>move_die</c>: moves a die (with everything on it) by a delta or to an absolute top-left
/// corner, undoable; refuses positions where it would overlap another die.
/// </summary>
public sealed class MoveDieTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public MoveDieTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "move_die";

    /// <inheritdoc/>
    public string Description =>
        "Move a die (name from list_dies) by dx/dy or to absolute x/y (µm, outline top-left). Undoable. " +
        "Refuses overlapping another die. To butt-couple two dies use link_dies instead.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            die = new { type = "string", description = "Die name from list_dies." },
            dx = new { type = "number" },
            dy = new { type = "number" },
            x = new { type = "number" },
            y = new { type = "number" },
        },
        required = new[] { "die" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var die = DieToolSupport.FindDie(_session, ToolArguments.GetString(arguments, "die"));
        var vm = _session.Canvas.Components.FirstOrDefault(c => c.Component == die);
        if (die == null || vm == null)
            return Task.FromResult(AgentToolResult.Error("No such die. Use list_dies."));

        double x = ToolArguments.GetDouble(arguments, "x") ?? vm.X + (ToolArguments.GetDouble(arguments, "dx") ?? 0);
        double y = ToolArguments.GetDouble(arguments, "y") ?? vm.Y + (ToolArguments.GetDouble(arguments, "dy") ?? 0);
        if (!_session.Canvas.CanMoveComponentTo(vm, x, y))
            return Task.FromResult(AgentToolResult.Error($"'{die.GroupName}' would overlap another die there."));

        _session.Commands.ExecuteCommand(new MoveComponentCommand(_session.Canvas, vm, vm.X, vm.Y, x, y));
        _session.Canvas.Dies.ApplyScopeBounds();
        _session.Canvas.NotifyDiesChanged();
        return Task.FromResult(AgentToolResult.Json(new
        {
            Die = DieToolSupport.Describe(die),
            Links = DieToolSupport.Links(_session).ToList(),
        }));
    }
}
