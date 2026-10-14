using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;

namespace CAP.Avalonia.Commands.Dies;

/// <summary>What a wire drawn between pins on two different dies becomes.</summary>
/// <param name="Command">The die-to-die link to execute (null when refused).</param>
/// <param name="RefusalKey">Localization key of the hint explaining a refusal (null when allowed).</param>
public sealed record FacetWireResult(IUndoableCommand? Command, string? RefusalKey);

/// <summary>
/// A wire dragged from one die to another is never a waveguide — a waveguide cannot cross a
/// die edge. Between two edge couplers whose facets face each other it becomes a die-to-die
/// link (the free-space facet coupling with its loss, exactly like <see cref="LinkDiesCommand"/>);
/// the far die is first aligned onto the near facet's axis at the link gap when it is not
/// there yet. Every other cross-die request is refused with a hint. One undo step.
/// </summary>
public static class FacetWireCommandFactory
{
    private const double AlignedToleranceMicrometers = 0.01;

    /// <summary>The link for a wire from <paramref name="start"/> to <paramref name="end"/> on different dies.</summary>
    public static FacetWireResult Create(DesignCanvasViewModel canvas, PhysicalPin start, PhysicalPin end)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var decision = DieWiringPolicy.Evaluate(start, end);
        if (decision.RefusalKey != null)
            return new FacetWireResult(null, decision.RefusalKey);
        if (decision.Verdict != DieWiringVerdict.FacetLink || decision.EndDie is not { } endDie)
            return new FacetWireResult(null, null);

        var steps = new List<IUndoableCommand>();
        var (dx, dy) = DieLinkSnapper.AlignmentFor(end, start);
        if (Math.Abs(dx) > AlignedToleranceMicrometers || Math.Abs(dy) > AlignedToleranceMicrometers)
        {
            var dieVm = canvas.Components.FirstOrDefault(c => ReferenceEquals(c.Component, endDie));
            if (dieVm == null || !canvas.CanMoveComponentTo(dieVm, dieVm.X + dx, dieVm.Y + dy))
                return new FacetWireResult(null, "Dies.Wiring.NoRoom");
            steps.Add(new MoveComponentCommand(canvas, dieVm, dieVm.X, dieVm.Y, dieVm.X + dx, dieVm.Y + dy));
        }

        var link = LinkDiesCommand.TryCreate(canvas, new[] { new DieLinkPair(end, start) });
        if (link == null)
            return new FacetWireResult(null, "Dies.Wiring.AlreadyLinked");
        steps.Add(link);
        return new FacetWireResult(new CompositeCommand(link.Description, steps), null);
    }
}
