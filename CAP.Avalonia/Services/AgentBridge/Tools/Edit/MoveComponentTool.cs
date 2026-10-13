using System.Globalization;
using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Edit;

/// <summary>
/// <c>move_component</c>: moves a top-level component or group by a delta or to an absolute
/// position through <see cref="MoveComponentCommand"/> on the shared undo history (so the
/// user can Ctrl+Z it), then re-routes and reports the new blocked state.
/// </summary>
public sealed class MoveComponentTool : IAgentBridgeTool
{
    private const double MinMoveMicrometers = 0.001;

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public MoveComponentTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "move_component";

    /// <inheritdoc/>
    public string Description =>
        "Move a top-level component or group (id from list_components) by dx/dy or to absolute x/y " +
        "(µm, top-left corner). Undoable (Ctrl+Z / undo tool). Refuses moves that would overlap another " +
        "component unless force=true. Waits for re-routing and returns the blocked connections afterwards.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            id = new { type = "string", description = "Component id (identifier) from list_components." },
            dx = new { type = "number", description = "Relative move in µm along x (use with dy)." },
            dy = new { type = "number", description = "Relative move in µm along y (use with dx)." },
            x = new { type = "number", description = "Absolute target x in µm (use with y instead of dx/dy)." },
            y = new { type = "number", description = "Absolute target y in µm." },
            force = new { type = "boolean", description = "Allow a move that overlaps another component. Default false." },
        },
        required = new[] { "id" },
    };

    /// <inheritdoc/>
    public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var id = ToolArguments.GetString(arguments, "id") ?? "";
        var vm = _session.FindComponent(id);
        if (vm == null)
            return AgentToolResult.Error($"No top-level component with id '{id}'. Use list_components.");
        if (vm.Component.IsLocked)
            return AgentToolResult.Error($"Component '{id}' is locked.");

        var target = ResolveTarget(arguments, vm.X, vm.Y);
        if (target == null)
            return AgentToolResult.Error("Pass either dx and dy, or absolute x and y.");
        var (newX, newY) = target.Value;
        if (Math.Abs(newX - vm.X) < MinMoveMicrometers && Math.Abs(newY - vm.Y) < MinMoveMicrometers)
            return AgentToolResult.Error("The target equals the current position; nothing to move.");
        if (!ToolArguments.GetBool(arguments, "force") && !_session.Canvas.CanMoveComponentTo(vm, newX, newY))
            return AgentToolResult.Error(string.Create(CultureInfo.InvariantCulture,
                $"Moving '{id}' to ({newX:F2}, {newY:F2}) would overlap another component or leave the chip. Pass force=true to move anyway."));

        var (oldX, oldY) = (vm.X, vm.Y);
        _session.Commands.ExecuteCommand(new MoveComponentCommand(_session.Canvas, vm, oldX, oldY, newX, newY));
        var routed = await RoutingAwaiter.RerouteAndWaitAsync(_session.Canvas);
        _session.Canvas.InvalidateSimulation();

        return AgentToolResult.Json(new
        {
            Moved = id,
            From = new { X = oldX, Y = oldY },
            To = new { X = vm.X, Y = vm.Y },
            RoutingComplete = routed,
            Routing = ConnectionSnapshot.Report(_session),
        });
    }

    private static (double X, double Y)? ResolveTarget(JsonElement args, double currentX, double currentY)
    {
        var x = ToolArguments.GetDouble(args, "x");
        var y = ToolArguments.GetDouble(args, "y");
        if (x.HasValue || y.HasValue)
            return (x ?? currentX, y ?? currentY);

        var dx = ToolArguments.GetDouble(args, "dx");
        var dy = ToolArguments.GetDouble(args, "dy");
        if (dx.HasValue || dy.HasValue)
            return (currentX + (dx ?? 0), currentY + (dy ?? 0));
        return null;
    }
}
