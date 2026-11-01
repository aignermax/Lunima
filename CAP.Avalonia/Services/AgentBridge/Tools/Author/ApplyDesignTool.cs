using System.Text.Json;
using System.Text.Json.Nodes;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Export;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Author;

/// <summary>
/// <c>apply_design</c>: builds a whole circuit from a declarative description in one call —
/// instances (explicit pose or pin-relative), parameters, light sources and wires — as Lunima JSON
/// or a gdsfactory netlist YAML. Runs the same tools an agent would call one by one, routes the
/// wires in one joint pass, and is atomic: on any failure every edit is undone.
/// </summary>
public sealed class ApplyDesignTool : IAgentBridgeTool
{
    private const int QuarterTurn = 90;

    private readonly AgentDesignSession _session;
    private readonly Func<AgentToolCatalog> _catalog;

    /// <summary>Creates the tool; the catalog is resolved lazily because it contains this tool.</summary>
    public ApplyDesignTool(AgentDesignSession session, Func<AgentToolCatalog> catalog)
    {
        _session = session;
        _catalog = catalog;
    }

    /// <inheritdoc/>
    public string Name => "apply_design";

    /// <inheritdoc/>
    public string Description =>
        "Build a circuit declaratively in one call. design (object), text (JSON or YAML) or path (.json/.yaml file):\n" +
        "{\"chip\": {\"width_um\", \"height_um\"}, \"instances\": {\"<name>\": {\"type\", \"pdk\"?, \"x\", \"y\", \"rotation_deg\"? | " +
        "\"attach_to\": \"<inst>.<pin>\", \"pin\", \"gap_um\"?, \"parameters\"?: {name: value}, \"light_source\"?: {enabled, " +
        "wavelength_nm, power}}}, \"connections\": [{\"from\": \"inst.pin\", \"to\": \"inst.pin\"}]}. " +
        "A gdsfactory netlist YAML (instances/placements/connections/routes, e.g. from export_netlist) works too. " +
        "Pin-relative instances must follow their target. clear=true starts from an empty design. Atomic.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            design = new { type = "object", description = "The design (see description)." },
            text = new { type = "string", description = "The design as JSON or YAML text." },
            path = new { type = "string", description = "A .json/.yaml/.yml file with the design." },
            clear = new { type = "boolean", description = "Start from an empty design. Default false." },
        },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) =>
        Task.FromResult(AgentToolResult.Error("apply_design runs only through the tool catalog."));

    /// <inheritdoc/>
    public async Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context)
    {
        var spec = ReadSpec(arguments, out var error);
        if (spec == null)
            return AgentToolResult.Error(error!);
        if (ToolArguments.GetBool(arguments, "clear") && _session.FileOperations is { } files
            && !await files.TryNewProjectAsync(() => { context.ThrowIfStopped(); return true; }))
            return AgentToolResult.Error("Could not start an empty design.");

        var run = new DesignRun(_catalog(), context);
        var undoBefore = _session.Commands.UndoCount;
        var failure = await BuildAsync(spec, run);
        if (failure != null)
        {
            while (_session.Commands.UndoCount > undoBefore
                && !(await run.CallAsync("undo", new JsonObject())).IsError) { }
            return AgentToolResult.Error($"{failure} — every edit of apply_design was undone.");
        }

        return AgentToolResult.Json(new
        {
            Placed = spec.Instances.Count,
            Wires = run.Bundle,
            IgnoredSettings = spec.Ignored,
        });
    }

    private async Task<string?> BuildAsync(DesignSpec spec, DesignRun run)
    {
        if (spec.Chip is { } chip
            && await run.FailsAsync("set_chip_size", new JsonObject { ["width_um"] = chip.Width, ["height_um"] = chip.Height }) is { } chipError)
            return chipError;

        foreach (var instance in spec.Instances)
        {
            var place = PlaceArguments(instance, out var error);
            if (place == null || await run.FailsAsync("place_component", place) is { } placeError)
                return $"instance '{instance.Name}': {error ?? run.LastError}";
            foreach (var (parameter, value) in instance.Parameters)
            {
                var failed = await run.FailsAsync("set_parameter",
                    new JsonObject { ["id"] = instance.Name, ["parameter"] = parameter, ["value"] = value });
                if (failed == null) continue;
                if (instance.Placement?.Gds != true) return $"instance '{instance.Name}': {failed}";
                spec.Ignored.Add($"{instance.Name}.{parameter}");
            }
            if (instance.LightSource is { } light)
            {
                var laser = (JsonObject)light.DeepClone();
                laser["id"] = instance.Name;
                laser["mark_as_source"] = true;
                if (await run.FailsAsync("set_light_source", laser) is { } lightError)
                    return $"instance '{instance.Name}': {lightError}";
            }
        }

        if (spec.Connections.Count == 0)
            return null;
        var wires = new JsonArray(spec.Connections.Select(c => (JsonNode)new JsonObject { ["from"] = c.From, ["to"] = c.To }).ToArray());
        return await run.FailsAsync("connect_bundle", new JsonObject { ["connections"] = wires }, keepResult: true);
    }

    /// <summary>The <c>place_component</c> arguments of one instance (gdsfactory poses converted).</summary>
    private JsonObject? PlaceArguments(DesignInstance instance, out string? error)
    {
        error = null;
        var template = ResolveType(instance.Type, instance.Arguments["pdk"]?.GetValue<string>());
        var place = (JsonObject)instance.Arguments.DeepClone();
        place["type"] = template?.Name ?? instance.Type;
        if (template != null) place["pdk"] = template.PdkSource;
        place["name"] = instance.Name;
        if (instance.Placement is not { } pose)
            return place;
        if (!pose.Gds)
        {
            (place["x"], place["y"], place["rotation_deg"]) = (pose.X, pose.Y, pose.Rotation);
            return place;
        }
        if (template == null)
        {
            error = $"unknown component '{instance.Type}' (no Lunima type, Nazca or gdsfactory function of that name).";
            return null;
        }
        // gdsfactory placements are cell origins in a y-up frame (export_netlist's convention).
        var appRotation = ((-(int)Math.Round(pose.Rotation) % 360) + 360) % 360;
        var probe = PlaceComponentCommand.CreateExact(_session.Canvas, template, 0, 0, appRotation / QuarterTurn).PlacedComponent!;
        var origin = NazcaCoordinateMapper.GetCellPlacement(probe, rawOverrideAnchor: null);
        (place["x"], place["y"], place["rotation_deg"]) = (Math.Round(pose.X - origin.X, 4), Math.Round(origin.Y - pose.Y, 4), appRotation);
        return place;
    }

    /// <summary>A library template by Lunima name, else by its Nazca or gdsfactory function.</summary>
    private ComponentTemplate? ResolveType(string type, string? pdk)
    {
        var candidates = _session.Templates.Where(t => pdk == null || string.Equals(t.PdkSource, pdk, StringComparison.OrdinalIgnoreCase)).ToList();
        return candidates.FirstOrDefault(t => string.Equals(t.Name, type, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(t => string.Equals(t.NazcaFunctionName, type, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(t => string.Equals(t.GdsFactoryFunction, type, StringComparison.OrdinalIgnoreCase));
    }

    private static DesignSpec? ReadSpec(JsonElement arguments, out string? error)
    {
        error = null;
        if (arguments.TryGetProperty("design", out var design) && design.ValueKind == JsonValueKind.Object)
            return DesignSpec.FromJson(JsonNode.Parse(design.GetRawText())!.AsObject(), out error);
        var text = ToolArguments.GetString(arguments, "text");
        var path = ToolArguments.GetString(arguments, "path");
        if (text == null && path != null)
        {
            if (!File.Exists(path)) { error = $"File not found: {Path.GetFullPath(path)}"; return null; }
            text = File.ReadAllText(path);
        }
        if (text == null) { error = "Pass design, text or path."; return null; }
        return DesignSpec.Parse(text, out error);
    }

    /// <summary>Nested tool calls of one apply_design run.</summary>
    private sealed class DesignRun(AgentToolCatalog catalog, AgentToolContext context)
    {
        public string? LastError { get; private set; }
        public JsonNode? Bundle { get; private set; }

        public Task<AgentToolResult> CallAsync(string tool, JsonObject arguments) =>
            catalog.InvokeWithinAsync(tool, JsonDocument.Parse(arguments.ToJsonString()).RootElement.Clone(), context);

        /// <summary>Runs the tool; returns its error text, or null on success.</summary>
        public async Task<string?> FailsAsync(string tool, JsonObject arguments, bool keepResult = false)
        {
            var result = await CallAsync(tool, arguments);
            var text = result.Content.FirstOrDefault(c => c.Type == "text")?.Text;
            if (keepResult && text != null) Bundle = JsonNode.Parse(text);
            LastError = result.IsError ? $"{tool}: {text}" : null;
            return LastError;
        }
    }
}
