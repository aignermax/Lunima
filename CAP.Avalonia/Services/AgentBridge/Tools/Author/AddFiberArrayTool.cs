using System.Text.Json;
using System.Text.Json.Nodes;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Author;

/// <summary>
/// <c>add_fiber_array</c>: brings device ports out to a row of fibre couplers at a fixed pitch
/// (gdsfactory's <c>add_fiber_array</c>): the couplers are placed beside the device, turned to
/// face it, ordered like the ports so the wires do not cross, and wired as one routed bundle.
/// Atomic: on failure nothing is left behind.
/// </summary>
public sealed class AddFiberArrayTool : IAgentBridgeTool
{
    private const double DefaultPitch = 127;
    private const double DefaultOffset = 250;
    private static readonly string[] CouplerPinHints = { "waveguide", "wg", "o1", "opt", "in" };

    private readonly AgentDesignSession _session;
    private readonly Func<AgentToolCatalog> _catalog;

    /// <summary>Creates the tool; the catalog is resolved lazily because it contains this tool.</summary>
    public AddFiberArrayTool(AgentDesignSession session, Func<AgentToolCatalog> catalog)
    {
        _session = session;
        _catalog = catalog;
    }

    /// <inheritdoc/>
    public string Name => "add_fiber_array";

    /// <inheritdoc/>
    public string Description =>
        "Route device ports to a fibre array: ports = [\"comp.pin\", ...], coupler = a grating/edge coupler type, " +
        "side = west|east|north|south of the device (default west), pitch_um (default 127), offset_um from the device " +
        "(default 250). Couplers are named <prefix>0..n in port order, turned to face the device and wired as one bundle. " +
        "Atomic. Returns the couplers and the bundle (lengths, skew, blocked).";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            ports = new { type = "array", items = new { type = "string" } },
            coupler = new { type = "string", description = "Coupler type (list_component_types filter=coupler)." },
            pdk = new { type = "string" },
            coupler_pin = new { type = "string", description = "The coupler's waveguide pin (default: guessed)." },
            side = new { type = "string", @enum = new[] { "west", "east", "north", "south" } },
            pitch_um = new { type = "number" },
            offset_um = new { type = "number" },
            prefix = new { type = "string", description = "Coupler name prefix (default 'fa')." },
        },
        required = new[] { "ports", "coupler" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) =>
        Task.FromResult(AgentToolResult.Error("add_fiber_array runs only through the tool catalog."));

    /// <inheritdoc/>
    public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context)
    {
        var plan = FiberArrayPlan.Create(_session, arguments, CouplerPinHints, DefaultPitch, DefaultOffset, out var error);
        if (plan == null)
            return AgentToolResult.Error(error!);

        var catalog = _catalog();
        var undoBefore = _session.Commands.UndoCount;
        var wires = new JsonArray();
        foreach (var site in plan.Sites)
        {
            var place = new JsonObject
            {
                ["type"] = plan.Coupler.Name, ["pdk"] = plan.Coupler.PdkSource, ["name"] = site.Name,
                ["x"] = site.X, ["y"] = site.Y, ["rotation_deg"] = PortAttachment.Degrees(plan.QuarterTurns),
            };
            var placed = await catalog.InvokeWithinAsync("place_component", Arguments(place), context);
            if (placed.IsError)
                return await FailAsync(catalog, context, undoBefore, $"{site.Name}: {placed.Content[0].Text}");
            wires.Add(new JsonObject { ["from"] = site.Port, ["to"] = $"{site.Name}.{plan.CouplerPin}" });
        }

        var bundle = await catalog.InvokeWithinAsync("connect_bundle", Arguments(new JsonObject { ["connections"] = wires }), context);
        if (bundle.IsError)
            return await FailAsync(catalog, context, undoBefore, bundle.Content[0].Text!);
        return AgentToolResult.Json(new
        {
            Couplers = plan.Sites.Select(s => new { s.Name, s.Port, s.X, s.Y }).ToList(),
            Bundle = JsonNode.Parse(bundle.Content[0].Text!),
        });
    }

    private async Task<AgentToolResult> FailAsync(AgentToolCatalog catalog, AgentToolContext context, int undoBefore, string reason)
    {
        while (_session.Commands.UndoCount > undoBefore
            && !(await catalog.InvokeWithinAsync("undo", Arguments(new JsonObject()), context)).IsError) { }
        return AgentToolResult.Error($"{reason} — the fibre array was removed again.");
    }

    private static JsonElement Arguments(JsonObject value) => JsonDocument.Parse(value.ToJsonString()).RootElement.Clone();
}
