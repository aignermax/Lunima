using CAP.Avalonia.Controls.Canvas.CutTool;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Routing.CrossingInsertion.ManualCrossing;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Cut;

/// <summary>
/// The crossing cell the agent's cuts insert — the same PDK crossing and the same geometry
/// rules (straight run each side, grid footprint clearance) as the Cut tool
/// (<see cref="CutToolCandidateComputer"/>), preferring the PDK the design is built from.
/// </summary>
public sealed class CrossingSupply
{
    private readonly IReadOnlyList<ComponentTemplate> _templates;
    private readonly IReadOnlyList<string> _preferredPdks;

    private CrossingSupply(ComponentTemplate template, IReadOnlyList<ComponentTemplate> templates,
        IReadOnlyList<string> preferredPdks, FootprintClearance? footprint)
    {
        Template = template;
        _templates = templates;
        _preferredPdks = preferredPdks;
        Footprint = footprint;
    }

    /// <summary>The crossing template.</summary>
    public ComponentTemplate Template { get; }

    /// <summary>Straight run needed on each side of a cut point (crossing half-extent + stub clearance), µm.</summary>
    public double RequiredRunMicrometers => CutToolCandidateComputer.ComputeRequiredRunMicrometers(Template);

    /// <summary>Grid clearance check for the crossing's bounding box, or null before the first routing grid exists.</summary>
    public FootprintClearance? Footprint { get; }

    /// <summary>A fresh crossing instance for one insertion (never reused).</summary>
    public CrossingComponentInstance? CreateInstance() =>
        CrossingComponentInstance.CreateFromTemplates(_templates, _preferredPdks);

    /// <summary>
    /// Resolves the crossing for the open design, or null when the loaded library has no
    /// crossing with all four wired ports (nothing could be inserted).
    /// </summary>
    public static CrossingSupply? Resolve(AgentDesignSession session)
    {
        var templates = session.Templates;
        if (CutToolCandidateComputer.FindUsableCrossingTemplate(templates) == null)
            return null;
        var preferred = CrossingComponentInstance.PreferredPdksOf(
            session.Canvas.Components.Select(c => c.Component), templates);
        var template = CrossingComponentInstance.FindCrossingTemplate(templates, preferred);
        if (template == null)
            return null;
        var footprint = CutToolCandidateComputer.BuildFootprintClearance(session.Canvas.Router.PathfindingGrid, template);
        return new CrossingSupply(template, templates, preferred, footprint);
    }
}
