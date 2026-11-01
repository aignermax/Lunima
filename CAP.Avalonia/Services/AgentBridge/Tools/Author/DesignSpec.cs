using System.Globalization;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Author;

/// <summary>One instance of a declarative design.</summary>
/// <param name="Name">Instance name (becomes the component's display name and id).</param>
/// <param name="Type">Component type: a Lunima type name, a Nazca function or a gdsfactory function.</param>
/// <param name="Placement">Explicit pose, or null when placed relative to a pin.</param>
/// <param name="Arguments">Further <c>place_component</c> arguments (pdk, attach_to, pin, gap_um).</param>
/// <param name="Parameters">Parameter values by name.</param>
/// <param name="LightSource">Laser settings (enabled, wavelength_nm, power), or null.</param>
public sealed record DesignInstance(string Name, string Type, (double X, double Y, double Rotation, bool Gds)? Placement,
    JsonObject Arguments, IReadOnlyDictionary<string, double> Parameters, JsonObject? LightSource);

/// <summary>
/// A declarative design: instances, how they are placed, and which pins are wired. Read from
/// Lunima's JSON form or from a gdsfactory netlist YAML (<c>instances</c> / <c>placements</c> /
/// <c>connections</c> / <c>routes</c>, as written by <c>export_netlist format=gdsfactory_yaml</c>).
/// </summary>
public sealed class DesignSpec
{
    /// <summary>Instances in the order they are placed (pin-relative ones after their targets).</summary>
    public List<DesignInstance> Instances { get; } = new();

    /// <summary>Wires as (<c>instance.pin</c>, <c>instance.pin</c>).</summary>
    public List<(string From, string To)> Connections { get; } = new();

    /// <summary>Chip size in µm, when given.</summary>
    public (double Width, double Height)? Chip { get; set; }

    /// <summary>gdsfactory settings Lunima could not apply (reported back).</summary>
    public List<string> Ignored { get; } = new();

    /// <summary>Parses JSON or YAML text into a spec; null with an error when malformed.</summary>
    public static DesignSpec? Parse(string text, out string? error)
    {
        JsonNode? root;
        error = null;
        try
        {
            var trimmed = text.TrimStart();
            root = trimmed.StartsWith('{') ? JsonNode.Parse(text) : YamlToJson(text);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or YamlDotNet.Core.YamlException)
        {
            error = $"The design is neither valid JSON nor YAML: {ex.Message}";
            return null;
        }
        return root is JsonObject design ? FromJson(design, out error) : Fail("A design is an object with 'instances'.", out error);
    }

    /// <summary>Builds a spec from an already parsed design object.</summary>
    public static DesignSpec? FromJson(JsonObject design, out string? error)
    {
        error = null;
        if (design["instances"] is not JsonObject instances || instances.Count == 0)
            return Fail("'instances' must map instance names to {type | component, ...}.", out error);

        var spec = new DesignSpec();
        if (design["chip"] is JsonObject chip && Number(chip["width_um"]) is double w && Number(chip["height_um"]) is double h)
            spec.Chip = (w, h);
        var placements = design["placements"] as JsonObject;
        foreach (var (name, node) in instances)
        {
            if (node is not JsonObject instance)
                return Fail($"Instance '{name}' must be an object.", out error);
            var parsed = ReadInstance(spec, name, instance, placements?[name] as JsonObject, out error);
            if (parsed == null)
                return null;
            spec.Instances.Add(parsed);
        }
        ReadConnections(spec, design);
        return spec;
    }

    private static DesignInstance? ReadInstance(DesignSpec spec, string name, JsonObject instance, JsonObject? gdsPlacement, out string? error)
    {
        error = null;
        var type = (instance["type"] ?? instance["component"])?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(type))
            return Fail<DesignInstance>($"Instance '{name}' needs a 'type' (or gdsfactory 'component').", out error);

        (double, double, double, bool)? placement = null;
        if (gdsPlacement != null)
            placement = (Number(gdsPlacement["x"]) ?? 0, Number(gdsPlacement["y"]) ?? 0, Number(gdsPlacement["rotation"]) ?? 0, true);
        else if (Number(instance["x"]) is double x && Number(instance["y"]) is double y)
            placement = (x, y, Number(instance["rotation_deg"]) ?? 0, false);
        else if (instance["attach_to"] == null)
            return Fail<DesignInstance>($"Instance '{name}' needs x and y, attach_to + pin, or a gdsfactory placement.", out error);

        var arguments = new JsonObject();
        foreach (var key in new[] { "pdk", "attach_to", "pin", "gap_um", "nudge" })
            if (instance[key] is { } value)
                arguments[key] = value.DeepClone();

        var parameters = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in new[] { instance["parameters"], instance["settings"] })
            if (source is JsonObject values)
                foreach (var (key, value) in values)
                {
                    if (Number(value) is double number) parameters[key] = number;
                    else spec.Ignored.Add($"{name}.{key}");
                }
        return new DesignInstance(name, type, placement, arguments, parameters, instance["light_source"] as JsonObject);
    }

    private static void ReadConnections(DesignSpec spec, JsonObject design)
    {
        switch (design["connections"])
        {
            case JsonArray list:
                foreach (var item in list.OfType<JsonObject>())
                    if (item["from"]?.GetValue<string>() is { } from && item["to"]?.GetValue<string>() is { } to)
                        spec.Connections.Add((from, to));
                break;
            case JsonObject map:
                AddLinks(spec, map);
                break;
        }
        if (design["routes"] is JsonObject routes)
            foreach (var (_, route) in routes)
                if (route?["links"] is JsonObject links)
                    AddLinks(spec, links);
    }

    /// <summary>gdsfactory links: <c>"inst,port": "inst,port"</c>.</summary>
    private static void AddLinks(DesignSpec spec, JsonObject links)
    {
        foreach (var (from, to) in links)
            if (to?.GetValue<string>() is { } target)
                spec.Connections.Add((from.Replace(',', '.'), target.Replace(',', '.')));
    }

    private static double? Number(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue(out double d) => d,
        JsonValue v when v.TryGetValue(out string? s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) => p,
        _ => null,
    };

    private static JsonNode? YamlToJson(string text)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        return stream.Documents.Count == 0 ? null : Convert(stream.Documents[0].RootNode);
    }

    private static JsonNode? Convert(YamlNode node) => node switch
    {
        YamlMappingNode map => new JsonObject(map.Children.Select(kv =>
            KeyValuePair.Create(((YamlScalarNode)kv.Key).Value ?? "", Convert(kv.Value)))),
        YamlSequenceNode list => new JsonArray(list.Children.Select(Convert).ToArray()),
        YamlScalarNode scalar => double.TryParse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? JsonValue.Create(d)
            : bool.TryParse(scalar.Value, out var b) ? JsonValue.Create(b) : JsonValue.Create(scalar.Value),
        _ => null,
    };

    private static DesignSpec? Fail(string message, out string? error) => Fail<DesignSpec>(message, out error);

    private static T? Fail<T>(string message, out string? error) where T : class
    {
        error = message;
        return null;
    }
}
