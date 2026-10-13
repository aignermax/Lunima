using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing.CrossingInsertion.ManualCrossing;

namespace CAP.Avalonia.Controls.Canvas.CutTool;

/// <summary>
/// The Cut tool without a pointer: builds the undoable command that cuts a given wire at a
/// given point by inserting a user-placed crossing there, with exactly the checks a click in
/// Cut mode applies (a usable PDK crossing, an axis-aligned straight under the point with room
/// for the crossing's ports, a clear footprint). Works on every wire, including the pieces of
/// routing-placed and user-placed crossings. For scripted callers (agent bridge, tests).
/// </summary>
public static class CutAtPoint
{
    /// <summary>Default distance (µm) the point may lie from the wire.</summary>
    public const double DefaultSnapDistanceMicrometers = 2.0;

    /// <summary>
    /// Creates the command that cuts <paramref name="connection"/> at the point of its nearest
    /// straight segment to (<paramref name="x"/>, <paramref name="y"/>); null with an
    /// <paramref name="error"/> when the cut is not possible there. The caller runs it through
    /// the <see cref="CommandManager"/> so it is undoable and the crossing undo journal sees it.
    /// </summary>
    /// <param name="canvas">The canvas holding the wire.</param>
    /// <param name="templates">The loaded component templates (the PDK crossing is taken from them).</param>
    /// <param name="connection">The wire to cut.</param>
    /// <param name="x">Cut point X (µm).</param>
    /// <param name="y">Cut point Y (µm).</param>
    /// <param name="error">Why no command was created.</param>
    /// <param name="snapDistanceMicrometers">How far the point may lie from the wire.</param>
    /// <param name="errorConsole">Optional sink for failures of the follow-up routing pass.</param>
    public static InsertManualCrossingCommand? CreateCommand(
        DesignCanvasViewModel canvas, IEnumerable<ComponentTemplate> templates, WaveguideConnection connection,
        double x, double y, out string? error,
        double snapDistanceMicrometers = DefaultSnapDistanceMicrometers, ErrorConsoleService? errorConsole = null)
    {
        error = null;
        var templateList = templates.ToList();
        if (!CutToolCandidateComputer.CollectEligibleConnections(canvas).Contains(connection))
        {
            error = "The connection is not an optical wire on the canvas.";
            return null;
        }
        var template = CutToolCandidateComputer.FindUsableCrossingTemplate(templateList);
        var instance = template == null ? null : CrossingComponentInstance.CreateFromTemplates(templateList);
        if (template == null || instance == null)
        {
            error = "No usable 4-port crossing is loaded in the PDK library.";
            return null;
        }

        var candidate = new ManualCrossingCandidateFinder().FindNearestFreeCandidate(
            new[] { connection }, (x, y), snapDistanceMicrometers,
            CutToolCandidateComputer.ComputeRequiredRunMicrometers(template),
            CutToolCandidateComputer.BuildFootprintClearance(canvas.Router.PathfindingGrid, template));
        if (candidate == null)
        {
            error = "No axis-aligned straight of the wire near the point has room for a crossing there.";
            return null;
        }
        return new InsertManualCrossingCommand(canvas, candidate, instance, errorConsole);
    }
}
