using CAP.Avalonia.Controls.Canvas.CutTool;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Connections;
using CAP_Core.Routing.CrossingInsertion.ManualCrossing;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Cut;

/// <summary>
/// The crossing cell the agent's cuts insert and the geometry rules a cut must satisfy —
/// exactly those of the Cut tool (<see cref="CutToolCandidateComputer"/>, <see cref="CutAtPoint"/>):
/// a straight run each side for the crossing's ports and a clear grid footprint.
/// </summary>
public sealed class CrossingSupply
{
    private CrossingSupply(ComponentTemplate template, FootprintClearance? footprint)
    {
        Template = template;
        Footprint = footprint;
    }

    /// <summary>The crossing template.</summary>
    public ComponentTemplate Template { get; }

    /// <summary>Straight run needed on each side of a cut point (crossing half-extent + stub clearance), µm.</summary>
    public double RequiredRunMicrometers => CutToolCandidateComputer.ComputeRequiredRunMicrometers(Template);

    /// <summary>Grid clearance check for the crossing's bounding box, or null before the first routing grid exists.</summary>
    public FootprintClearance? Footprint { get; }

    /// <summary>
    /// Whether the crossing's bounding box at <paramref name="point"/> is free of components and of
    /// wires other than the cut wire and the wire that will pass through (it is re-docked onto the
    /// crossing, so its old path does not count) — the Cut tool's footprint rule plus that exemption.
    /// </summary>
    public bool IsFootprintClear((double X, double Y) point, WaveguideConnection cut, WaveguideConnection? through)
    {
        if (Footprint is not { } clearance)
            return true;
        var half = clearance.HalfExtentMicrometers + clearance.Grid.ObstaclePaddingMicrometers;
        var allowed = new HashSet<Guid> { cut.Id };
        if (through != null)
            allowed.Add(through.Id);
        return clearance.Grid.IsAreaClearForCrossing(point.X - half, point.Y - half, point.X + half, point.Y + half, allowed);
    }

    /// <summary>
    /// Resolves the crossing for the open design, or null when the loaded library has no
    /// crossing with all four wired ports (nothing could be inserted).
    /// </summary>
    public static CrossingSupply? Resolve(AgentDesignSession session)
    {
        var template = CutToolCandidateComputer.FindUsableCrossingTemplate(session.Templates);
        if (template == null)
            return null;
        return new CrossingSupply(template,
            CutToolCandidateComputer.BuildFootprintClearance(session.Canvas.Router.PathfindingGrid, template));
    }
}
