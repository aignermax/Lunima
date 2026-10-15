using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;

namespace CAP.Avalonia.Commands.Dies;

/// <summary>
/// Creates the die-to-die link connection for every facing facet pair a die snap aligned
/// (pairs already linked are skipped). Facet links are never routed — their transmission is
/// the free-space coupling across the gap — so they appear as soon as the dies touch.
/// </summary>
public sealed class LinkDiesCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly IReadOnlyList<CreateConnectionCommand> _links;

    private LinkDiesCommand(DesignCanvasViewModel canvas, IReadOnlyList<CreateConnectionCommand> links)
    {
        _canvas = canvas;
        _links = links;
    }

    /// <summary>The command for the unlinked pairs among <paramref name="pairs"/>, or null when all are linked.</summary>
    public static LinkDiesCommand? TryCreate(DesignCanvasViewModel canvas, IEnumerable<DieLinkPair> pairs)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(pairs);
        var links = pairs
            .Where(p => !IsLinked(canvas, p.MovingFacet) && !IsLinked(canvas, p.FixedFacet))
            .Select(p => new CreateConnectionCommand(canvas, p.MovingFacet, p.FixedFacet))
            .ToList();
        return links.Count == 0 ? null : new LinkDiesCommand(canvas, links);
    }

    /// <summary>Number of links this command creates.</summary>
    public int LinkCount => _links.Count;

    /// <inheritdoc/>
    public string Description => _links.Count == 1 ? "Link dies" : $"Link dies ({_links.Count} couplers)";

    /// <inheritdoc/>
    public void Execute()
    {
        foreach (var link in _links)
            link.Execute();
        _canvas.InvalidateSimulation();
        _canvas.NotifyDiesChanged();
    }

    /// <inheritdoc/>
    public void Undo()
    {
        for (int i = _links.Count - 1; i >= 0; i--)
            _links[i].Undo();
        _canvas.InvalidateSimulation();
        _canvas.NotifyDiesChanged();
    }

    private static bool IsLinked(DesignCanvasViewModel canvas, PhysicalPin pin) =>
        canvas.ConnectionManager.Connections.Any(c => c.StartPin == pin || c.EndPin == pin);
}
