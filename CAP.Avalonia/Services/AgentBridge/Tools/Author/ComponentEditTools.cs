using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Author;

/// <summary><c>get_component</c>: one component's full description — pose, pins and their wires, parameters, light source.</summary>
public sealed class GetComponentTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public GetComponentTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "get_component";

    /// <inheritdoc/>
    public string Description =>
        "Describe one top-level component or group: type, pdk, position/size/rotation, every pin with its absolute " +
        "position (µm), facing angle and the pin it is wired to, tunable parameters, and light-source settings.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            id = new { type = "string", description = "Component id or unique display name." },
            pins = new { type = "string", @enum = new[] { "all", "signals", "connected", "free" }, description = "Which pins to list (large cells): default all." },
        },
        required = new[] { "id" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var id = ToolArguments.GetString(arguments, "id") ?? "";
        var vm = _session.FindComponent(id);
        return Task.FromResult(vm == null
            ? AgentToolResult.Error($"No top-level component with id '{id}'. Use list_components.")
            : AgentToolResult.Json(ComponentDescriber.Describe(_session, vm, ToolArguments.GetString(arguments, "pins"))));
    }
}

/// <summary><c>rotate_component</c>: rotates a component by quarter turns counter-clockwise, undoably, then re-routes.</summary>
public sealed class RotateComponentTool : IAgentBridgeTool
{
    private const int QuarterTurn = 90;

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public RotateComponentTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "rotate_component";

    /// <inheritdoc/>
    public string Description =>
        "Rotate a top-level component counter-clockwise by degrees (90, 180 or 270; default 90). Refused when the " +
        "component is locked or the rotated footprint would collide. Undoable; re-routes its wires.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            id = new { type = "string", description = "Component id or unique display name." },
            degrees = new { type = "integer", description = "90, 180 or 270 (counter-clockwise). Default 90." },
        },
        required = new[] { "id" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(arguments, null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) => ExecuteCoreAsync(arguments, context);

    private async Task<AgentToolResult> ExecuteCoreAsync(JsonElement arguments, AgentToolContext? context)
    {
        var id = ToolArguments.GetString(arguments, "id") ?? "";
        var vm = _session.FindComponent(id);
        if (vm == null)
            return AgentToolResult.Error($"No top-level component with id '{id}'. Use list_components.");
        var degrees = (int)(ToolArguments.GetDouble(arguments, "degrees") ?? QuarterTurn);
        if (degrees % QuarterTurn != 0 || degrees <= 0 || degrees >= 360)
            return AgentToolResult.Error("degrees must be 90, 180 or 270.");

        var component = vm.Component;
        if (component.IsLocked)
            return AgentToolResult.Error($"'{id}' is locked.");
        // Every quarter turn keeps the top-left corner, so each intermediate footprint is either
        // the current one or the swapped one: checking the swapped one covers all steps.
        if (!_session.Canvas.CanPlaceComponent(vm.X, vm.Y, component.HeightMicrometers, component.WidthMicrometers, vm))
            return AgentToolResult.Error($"'{id}' cannot rotate here: the rotated footprint would collide or leave the chip.");

        await using var edit = new AgentDocumentOperation(_session.Canvas, context);
        var turns = Enumerable.Range(0, degrees / QuarterTurn)
            .Select(_ => (IUndoableCommand)new RotateComponentCommand(_session.Canvas, vm))
            .ToList();
        edit.Commit(() => _session.Commands.ExecuteCommand(new CompositeCommand($"Rotate {vm.Name} by {degrees}°", turns)));
        var routed = await edit.RouteAsync();
        edit.Commit(_session.Canvas.InvalidateSimulation);
        return AgentToolResult.Json(new
        {
            Rotated = ComponentDescriber.Describe(_session, vm),
            RoutingComplete = routed,
            Routing = ConnectionSnapshot.Report(_session),
        });
    }
}
