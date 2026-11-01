using System.Text.Json;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Author;

/// <summary>
/// <c>compare_netlist</c>: layout-versus-schematic for agents. Compares the open design (e.g. a GDS
/// just imported with <c>import_gds</c>) with a reference netlist — a gdsfactory YAML or Lunima
/// design JSON: instance count per component type, and the wiring as type-level connections
/// (<c>Type.pin — Type.pin</c>), so it works without matching instance names. When the instance
/// names do match, the wires are also compared one by one.
/// </summary>
public sealed class CompareNetlistTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public CompareNetlistTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "compare_netlist";

    /// <inheritdoc/>
    public string Description =>
        "LVS-style check: compare the open design with a reference netlist (design object, text or path: gdsfactory " +
        "YAML or Lunima design JSON). Reports per component type the count in layout vs reference, wires missing from " +
        "or extra in the layout (as Type.pin—Type.pin), and — where instance names match — exact per-instance wire " +
        "differences. match=true when everything agrees.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            design = new { type = "object" },
            text = new { type = "string" },
            path = new { type = "string" },
        },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var reference = ApplyDesignTool.ReadSpec(arguments, out var error);
        if (reference == null)
            return Task.FromResult(AgentToolResult.Error(error!));

        var referenceTypes = reference.Instances.ToDictionary(i => i.Name,
            i => TemplateLookup.ResolveAny(_session.Templates, i.Type)?.Name ?? i.Type, StringComparer.OrdinalIgnoreCase);
        var layoutWires = _session.Canvas.Connections
            .Select(c => (c.Connection.StartPin, c.Connection.EndPin))
            .ToList();

        var typeCounts = Compare(
            _session.Canvas.Components.Select(c => TypeOf(c.Component)),
            referenceTypes.Values);
        var typeWires = CompareWires(
            layoutWires.Select(w => Edge($"{TypeOf(w.StartPin.ParentComponent)}.{w.StartPin.Name}", $"{TypeOf(w.EndPin.ParentComponent)}.{w.EndPin.Name}")),
            reference.Connections.Select(c => Edge(Typed(c.From, referenceTypes), Typed(c.To, referenceTypes))));
        var named = NamedWires(layoutWires, reference);

        var match = typeCounts.All(t => t.Layout == t.Reference) && typeWires.Missing.Count == 0 && typeWires.Extra.Count == 0
            && (named == null || (named.Missing.Count == 0 && named.Extra.Count == 0));
        return Task.FromResult(AgentToolResult.Json(new
        {
            Match = match,
            Types = typeCounts.Where(t => t.Layout != t.Reference).ToList(),
            TypesChecked = typeCounts.Count,
            MissingWires = typeWires.Missing,
            ExtraWires = typeWires.Extra,
            ByInstanceName = named,
        }));
    }

    private sealed record TypeCount(string Type, int Layout, int Reference);

    private sealed record WireDiff(List<string> Missing, List<string> Extra);

    private static List<TypeCount> Compare(IEnumerable<string> layout, IEnumerable<string> reference)
    {
        var l = layout.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        var r = reference.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        return l.Keys.Union(r.Keys).OrderBy(t => t)
            .Select(t => new TypeCount(t, l.GetValueOrDefault(t), r.GetValueOrDefault(t))).ToList();
    }

    /// <summary>Multiset difference: wires the reference has more of (missing) and the layout has more of (extra).</summary>
    private static WireDiff CompareWires(IEnumerable<string> layout, IEnumerable<string> reference)
    {
        var remaining = reference.GroupBy(e => e).ToDictionary(g => g.Key, g => g.Count());
        var extra = new List<string>();
        foreach (var edge in layout)
        {
            if (remaining.TryGetValue(edge, out var n) && n > 0) remaining[edge] = n - 1;
            else extra.Add(edge);
        }
        var missing = remaining.SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value)).ToList();
        return new WireDiff(missing, extra);
    }

    /// <summary>Exact comparison when every reference instance name names a component of the design.</summary>
    private WireDiff? NamedWires(List<(PhysicalPin StartPin, PhysicalPin EndPin)> layoutWires, DesignSpec reference)
    {
        if (reference.Instances.Any(i => _session.FindComponent(i.Name) == null))
            return null;
        var names = reference.Instances.ToDictionary(i => _session.FindComponent(i.Name)!.Component, i => i.Name);
        string Name(PhysicalPin pin) => names.TryGetValue(TopLevel(pin.ParentComponent), out var n) ? $"{n}.{pin.Name}" : $"?.{pin.Name}";
        return CompareWires(layoutWires.Select(w => Edge(Name(w.StartPin), Name(w.EndPin))),
            reference.Connections.Select(c => Edge(c.From, c.To)));
    }

    private static string Typed(string reference, IReadOnlyDictionary<string, string> types)
    {
        var dot = reference.LastIndexOf('.');
        return dot > 0 && types.TryGetValue(reference[..dot], out var type) ? $"{type}{reference[dot..]}" : reference;
    }

    private static string TypeOf(Component component) =>
        component is ComponentGroup group ? $"group:{group.GroupName}" : component.TemplateName ?? component.NazcaFunctionName;

    private static Component TopLevel(Component component)
    {
        var current = component;
        while (current.ParentGroup is Component parent) current = parent;
        return current;
    }

    private static string Edge(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? $"{a} — {b}" : $"{b} — {a}";
}
