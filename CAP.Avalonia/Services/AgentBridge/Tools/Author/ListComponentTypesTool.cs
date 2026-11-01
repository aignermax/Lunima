using System.Text.Json;
using CAP.Avalonia.ViewModels.Library;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Author;

/// <summary>
/// <c>list_component_types</c>: the component library an agent can place from — every loaded
/// PDK template with its footprint, pins (local offsets and facing) and tunable parameters,
/// and whether the active fabrication process allows placing it.
/// </summary>
public sealed class ListComponentTypesTool : IAgentBridgeTool
{
    private const int DefaultLimit = 60;

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public ListComponentTypesTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "list_component_types";

    /// <inheritdoc/>
    public string Description =>
        "List placeable component types (the loaded PDK libraries) and library groups/gates: name, pdk, category, footprint " +
        "(µm), pins with local offset from the top-left corner and facing angle, and tunable parameters. " +
        "Filter by text (name/category), pdk or category. allowed=false means the active process forbids it.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            filter = new { type = "string", description = "Case-insensitive substring of the name or category." },
            pdk = new { type = "string", description = "Only this PDK (e.g. 'Built-in', 'SiEPIC EBeam PDK')." },
            category = new { type = "string", description = "Only this category." },
            include_pins = new { type = "boolean", description = "Include pins and parameters. Default true." },
            limit = new { type = "integer", description = $"Maximum entries (default {DefaultLimit})." },
            include_groups = new { type = "boolean", description = "Also list library groups and gates (saved cells), placeable with place_component. Default true." },
        },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var filter = ToolArguments.GetString(arguments, "filter");
        var pdk = ToolArguments.GetString(arguments, "pdk");
        var category = ToolArguments.GetString(arguments, "category");
        var includePins = ToolArguments.GetBool(arguments, "include_pins", fallback: true);
        var limit = (int)(ToolArguments.GetDouble(arguments, "limit") ?? DefaultLimit);

        var matching = _session.Templates
            .Where(t => Contains(t.Name, filter) || Contains(t.Category, filter))
            .Where(t => pdk == null || string.Equals(t.PdkSource, pdk, StringComparison.OrdinalIgnoreCase))
            .Where(t => category == null || string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var placement = _session.Placement;

        return Task.FromResult(AgentToolResult.Json(new
        {
            Total = matching.Count,
            Pdks = _session.Templates.Select(t => t.PdkSource).Distinct().OrderBy(p => p).ToList(),
            Types = matching.Take(Math.Max(1, limit)).Select(t => Describe(t, placement.CheckPlacement(t.PdkSource).IsAllowed, includePins)).ToList(),
            Groups = ToolArguments.GetBool(arguments, "include_groups", fallback: true) ? DescribeGroups(filter) : null,
        }));
    }

    /// <summary>Library groups (saved cells and gates) with their external pins.</summary>
    private IReadOnlyList<object>? DescribeGroups(string? filter) =>
        _session.Main?.LeftPanel.ComponentLibrary.GetLibraryManager().Templates
            .Where(t => Contains(t.Name, filter) || Contains(t.Category, filter))
            .Select(t => (object)new
            {
                t.Name,
                t.Source,
                t.Category,
                Width = t.WidthMicrometers,
                Height = t.HeightMicrometers,
                Components = t.ComponentCount,
                IsGate = t.TemplateGroup?.TruthTablePinAssignment != null,
                Pins = t.TemplateGroup?.ExternalPins.Select(p => new { p.Name, OffsetX = p.RelativeX, OffsetY = p.RelativeY, AngleDeg = p.AngleDegrees }).ToList(),
            })
            .ToList();

    private static bool Contains(string text, string? filter) =>
        filter == null || text.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static object Describe(ComponentTemplate template, bool allowed, bool includePins) => new
    {
        template.Name,
        Pdk = template.PdkSource,
        template.Category,
        Width = template.WidthMicrometers,
        Height = template.HeightMicrometers,
        Allowed = allowed,
        Pins = includePins
            ? template.PinDefinitions.Select(p => new
            {
                p.Name,
                OffsetX = p.OffsetX,
                OffsetY = p.OffsetY,
                AngleDeg = p.AngleDegrees,
                Domain = p.Kind.ToString().ToLowerInvariant(),
            }).ToList()
            : null,
        Parameters = includePins
            ? template.ParameterDefinitions.Select(d => new { d.Name, d.Label, d.Unit, Default = d.DefaultValue, Min = d.MinValue, Max = d.MaxValue, Slider = d.SliderNumber }).ToList()
            : null,
    };
}
