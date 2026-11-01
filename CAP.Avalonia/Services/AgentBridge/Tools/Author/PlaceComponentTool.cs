using System.Globalization;
using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Author;

/// <summary>
/// <c>place_component</c>: places a library component at an exact top-left position with a
/// rotation, through <see cref="PlaceComponentCommand"/> on the shared undo history. A spot that
/// is taken is refused (or, with <c>nudge=true</c>, moved to the nearest free spot) — never
/// silently overlapped. Supersedes the in-app assistant's centred, rotation-less variant.
/// </summary>
public sealed class PlaceComponentTool : IAgentBridgeTool
{
    private const int QuarterTurn = 90;
    private const double SamePositionTolerance = 0.001;

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public PlaceComponentTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "place_component";

    /// <inheritdoc/>
    public string Description =>
        "Place a component type or a library group/gate (from list_component_types) with its top-left corner at x,y (µm), rotated " +
        "rotation_deg counter-clockwise (0/90/180/270). Refuses an occupied spot unless nudge=true (moves to the " +
        "nearest free spot). Optional name sets the display name. Undoable. Returns the new component with its pins.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            type = new { type = "string", description = "Component type name from list_component_types." },
            pdk = new { type = "string", description = "PDK of the type when the name exists in several." },
            x = new { type = "number", description = "Top-left x in µm." },
            y = new { type = "number", description = "Top-left y in µm." },
            rotation_deg = new { type = "integer", description = "Counter-clockwise rotation: 0, 90, 180 or 270. Default 0." },
            name = new { type = "string", description = "Optional display name (usable as id in other tools when unique)." },
            nudge = new { type = "boolean", description = "Move to the nearest free spot when x,y is occupied. Default false." },
        },
        required = new[] { "type", "x", "y" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(arguments, null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) => ExecuteCoreAsync(arguments, context);

    private async Task<AgentToolResult> ExecuteCoreAsync(JsonElement arguments, AgentToolContext? context)
    {
        var type = ToolArguments.GetString(arguments, "type");
        var template = TemplateLookup.Find(_session.Templates, type, ToolArguments.GetString(arguments, "pdk"), out var error);
        if (template == null)
        {
            var library = _session.Main?.LeftPanel.ComponentLibrary.GetLibraryManager();
            var groupTemplate = library?.Templates.FirstOrDefault(t => string.Equals(t.Name, type, StringComparison.OrdinalIgnoreCase));
            return groupTemplate != null
                ? await new GroupTemplatePlacement(_session, library!).PlaceAsync(groupTemplate, arguments, context)
                : AgentToolResult.Error(error!);
        }
        var (allowed, reason) = _session.Placement.CheckPlacement(template.PdkSource);
        if (!allowed)
            return AgentToolResult.Error(reason ?? $"'{template.Name}' belongs to another fabrication process.");

        var x = ToolArguments.GetDouble(arguments, "x");
        var y = ToolArguments.GetDouble(arguments, "y");
        if (x == null || y == null)
            return AgentToolResult.Error("Pass x and y (µm, top-left corner).");
        var rotation = (int)(ToolArguments.GetDouble(arguments, "rotation_deg") ?? 0);
        if (rotation % QuarterTurn != 0)
            return AgentToolResult.Error("rotation_deg must be 0, 90, 180 or 270.");
        var quarterTurns = ((rotation / QuarterTurn) % 4 + 4) % 4;

        var command = ResolvePlacement(template, x.Value, y.Value, quarterTurns,
            ToolArguments.GetBool(arguments, "nudge"), out error);
        if (command == null)
            return AgentToolResult.Error(error!);

        await using var edit = new AgentDocumentOperation(_session.Canvas, context);
        edit.Commit(() => _session.Commands.ExecuteCommand(command));
        var placed = command.CreatedViewModel!;
        var name = ToolArguments.GetString(arguments, "name");
        if (!string.IsNullOrWhiteSpace(name))
            edit.Commit(() => _session.Commands.ExecuteCommand(new RenameComponentCommand(placed.Component, name)));
        edit.Commit(_session.Canvas.InvalidateSimulation);

        return AgentToolResult.Json(new
        {
            Placed = ComponentDescriber.Describe(_session, placed),
            Nudged = Math.Abs(placed.X - x.Value) > SamePositionTolerance || Math.Abs(placed.Y - y.Value) > SamePositionTolerance,
        });
    }

    private PlaceComponentCommand? ResolvePlacement(
        ComponentTemplate template, double x, double y, int quarterTurns, bool nudge, out string? error)
    {
        error = null;
        var exact = PlaceComponentCommand.CreateExact(_session.Canvas, template, x, y, quarterTurns);
        var footprint = exact.PlacedComponent!;
        var candidate = new ComponentViewModel(footprint);
        var free = _session.Canvas.FindValidPlacement(x, y, footprint.WidthMicrometers, footprint.HeightMicrometers, candidate);
        if (free == null)
        {
            error = "No free spot for this component on the chip.";
            return null;
        }

        var (freeX, freeY) = free.Value;
        if (Math.Abs(freeX - x) < SamePositionTolerance && Math.Abs(freeY - y) < SamePositionTolerance)
            return exact;
        if (nudge)
            return PlaceComponentCommand.CreateExact(_session.Canvas, template, freeX, freeY, quarterTurns);

        error = string.Create(CultureInfo.InvariantCulture,
            $"({x:F2}, {y:F2}) is occupied or off the chip. Nearest free top-left: ({freeX:F2}, {freeY:F2}); pass nudge=true to use it.");
        return null;
    }
}
