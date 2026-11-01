using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP_Core.Components.Creation;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Author;

/// <summary>
/// Places a library group (a saved cell or gate prefab) for <c>place_component</c>: the same
/// <see cref="PlaceGroupTemplateCommand"/> as dragging it from the library, at a top-left
/// position, then the optional rotation and display name — one undo step each.
/// </summary>
public sealed class GroupTemplatePlacement
{
    private const int QuarterTurn = 90;
    private const double SamePositionTolerance = 0.001;

    private readonly AgentDesignSession _session;
    private readonly IGroupLibrary _library;

    /// <summary>Creates the placement for <paramref name="session"/> from <paramref name="library"/>.</summary>
    public GroupTemplatePlacement(AgentDesignSession session, IGroupLibrary library)
    {
        _session = session;
        _library = library;
    }

    /// <summary>Places <paramref name="template"/> as described by the tool arguments.</summary>
    public async Task<AgentToolResult> PlaceAsync(GroupTemplate template, JsonElement arguments, AgentToolContext? context)
    {
        var x = ToolArguments.GetDouble(arguments, "x");
        var y = ToolArguments.GetDouble(arguments, "y");
        if (x == null || y == null)
            return AgentToolResult.Error("Pass x and y (µm, top-left corner).");
        var rotation = (int)(ToolArguments.GetDouble(arguments, "rotation_deg") ?? 0);
        if (rotation % QuarterTurn != 0)
            return AgentToolResult.Error("rotation_deg must be 0, 90, 180 or 270.");

        // The command centres the group on the point it is given.
        var command = PlaceGroupTemplateCommand.TryCreate(_session.Canvas, _library, template,
            x.Value + template.WidthMicrometers / 2, y.Value + template.HeightMicrometers / 2, out var rejection);
        if (command == null)
            return AgentToolResult.Error(rejection != null
                ? $"'{template.Name}' cannot be instantiated: {rejection.Message}"
                : $"No free spot for '{template.Name}' near ({x}, {y}).");

        await using var edit = new AgentDocumentOperation(_session.Canvas, context);
        edit.Commit(() => _session.Commands.ExecuteCommand(command));
        var placed = _session.Canvas.Components.First(c => c.Component == command.GroupToPlace);
        var turns = ((rotation / QuarterTurn) % 4 + 4) % 4;
        if (turns > 0)
        {
            var steps = Enumerable.Range(0, turns).Select(_ => (IUndoableCommand)new RotateComponentCommand(_session.Canvas, placed)).ToList();
            edit.Commit(() => _session.Commands.ExecuteCommand(new CompositeCommand($"Rotate {template.Name}", steps)));
        }
        var name = ToolArguments.GetString(arguments, "name");
        if (!string.IsNullOrWhiteSpace(name))
            edit.Commit(() => _session.Commands.ExecuteCommand(new RenameComponentCommand(placed.Component, name)));
        var routed = await edit.RouteAsync();
        edit.Commit(_session.Canvas.InvalidateSimulation);

        return AgentToolResult.Json(new
        {
            Placed = ComponentDescriber.Describe(_session, placed),
            Nudged = Math.Abs(placed.X - x.Value) > SamePositionTolerance || Math.Abs(placed.Y - y.Value) > SamePositionTolerance,
            RoutingComplete = routed,
        });
    }
}
