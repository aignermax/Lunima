using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;
using CAP_Core.Components.Connections;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Author;

/// <summary>
/// <c>connect_bundle</c>: creates many pin-to-pin wires in one undo step and routes them in a
/// single joint routing pass (Lunima's router routes all nets together), then reports each wire
/// and the bundle's length spread — the counterpart of gdsfactory's <c>route_bundle</c>.
/// </summary>
public sealed class ConnectBundleTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public ConnectBundleTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "connect_bundle";

    /// <inheritdoc/>
    public string Description =>
        "Create several wires at once, routed together in one pass (one undo step): connections is a list of " +
        "{from: \"component.pin\", to: \"component.pin\"}. Returns each wire (length, loss, blocked + reason) and the " +
        "bundle's min/max length and skew — use match_lengths afterwards when equal lengths matter.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            connections = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new { from = new { type = "string" }, to = new { type = "string" } },
                    required = new[] { "from", "to" },
                },
            },
        },
        required = new[] { "connections" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(arguments, null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) => ExecuteCoreAsync(arguments, context);

    private async Task<AgentToolResult> ExecuteCoreAsync(JsonElement arguments, AgentToolContext? context)
    {
        if (!arguments.TryGetProperty("connections", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
            return AgentToolResult.Error("Pass connections: [{\"from\": \"a.out\", \"to\": \"b.in\"}, ...].");

        var commands = new List<CreateConnectionCommand>();
        var pairs = new List<(CAP_Core.Components.Core.PhysicalPin From, CAP_Core.Components.Core.PhysicalPin To)>();
        var index = 0;
        foreach (var item in list.EnumerateArray())
        {
            var from = Resolve(item, "from", out var error);
            var to = error == null ? Resolve(item, "to", out error) : null;
            if (error != null)
                return AgentToolResult.Error($"connections[{index}]: {error}");
            var command = new CreateConnectionCommand(_session.Canvas, from!, to!);
            if (!command.ArePinKindsCompatible || command.CrossesDieEdge)
                return AgentToolResult.Error($"connections[{index}]: the pins are in different domains or on different dies.");
            commands.Add(command);
            pairs.Add((from!, to!));
            index++;
        }

        await using var edit = new AgentDocumentOperation(_session.Canvas, context);
        edit.Commit(() => _session.Commands.ExecuteCommand(
            new CompositeCommand($"Connect bundle of {commands.Count}", commands.Cast<IUndoableCommand>().ToList())));
        foreach (var command in commands)
            await command.HomingCompletion;
        var routed = await edit.RouteAsync();
        edit.Commit(_session.Canvas.InvalidateSimulation);

        var wires = pairs.Select(p => _session.Canvas.Connections.Select(v => v.Connection)
                .FirstOrDefault(w => Joins(w, p.From, p.To)))
            .Select(w => w == null ? null : DesignDescriber.Describe(w, isCrossingPiece: false))
            .ToList();
        var lengths = wires.Where(w => w != null).Select(w => w!.LengthUm).ToList();
        return AgentToolResult.Json(new
        {
            Count = commands.Count,
            Blocked = wires.Count(w => w?.Blocked == true),
            MinLengthUm = lengths.Count > 0 ? Math.Round(lengths.Min(), 3) : (double?)null,
            MaxLengthUm = lengths.Count > 0 ? Math.Round(lengths.Max(), 3) : (double?)null,
            SkewUm = lengths.Count > 0 ? Math.Round(lengths.Max() - lengths.Min(), 3) : (double?)null,
            RoutingComplete = routed,
            Connections = wires,
        });
    }

    private static bool Joins(WaveguideConnection wire, CAP_Core.Components.Core.PhysicalPin a, CAP_Core.Components.Core.PhysicalPin b) =>
        (wire.StartPin == a && wire.EndPin == b) || (wire.StartPin == b && wire.EndPin == a);

    private CAP_Core.Components.Core.PhysicalPin? Resolve(JsonElement item, string key, out string? error)
    {
        error = null;
        var reference = ToolArguments.GetString(item, key);
        var dot = reference?.LastIndexOf('.') ?? -1;
        if (reference == null || dot <= 0)
        {
            error = $"{key} must be \"component.pin\".";
            return null;
        }
        var component = _session.FindComponent(reference[..dot]);
        var pin = component == null ? null : ComponentPins.Find(component.Component, reference[(dot + 1)..]);
        if (pin == null)
            error = $"No pin '{reference}'. Use get_component for pin names.";
        return pin?.Pin;
    }
}
