using CAP_Core.Components.Core;
using CAP_Core.Components.Connections.Ownership;
using CAP_Core.Components.Dies;

namespace CAP.Avalonia.ViewModels.Canvas.DieSurface;

/// <summary>
/// What zoom-to-fit (F) frames. In the System workspace the unit of work is the die, so F
/// frames every die. Elsewhere the parts are the unit of work: F frames the content of the
/// current die — the die of the selection, else the die under the cursor, else the die worked
/// on last — and only falls back to all content when no die is current. An empty die frames
/// its outline. A design without dies (or inside a group) frames its components as always.
/// </summary>
public static class ZoomToFitTarget
{
    /// <summary>The rectangle to fit, or null when there is nothing to frame.</summary>
    /// <param name="canvas">The design canvas.</param>
    /// <param name="fitsDies">True in the System workspace (frame all dies).</param>
    /// <param name="cursor">Canvas position of the pointer, or null when it is not over the canvas.</param>
    public static BoundingBox? Resolve(DesignCanvasViewModel canvas, bool fitsDies, (double X, double Y)? cursor)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (!canvas.Dies.IsAssemblyRoot)
            return fitsDies && canvas.ChipDie.IsShown ? ToBox(canvas.ChipDie.Outline) : BoundingBoxCalculator.Calculate(canvas.Components);
        var dies = canvas.Dies.RootDies;
        if (fitsDies)
            return ToBox(DieAssembly.CarrierOutline(dies));
        var die = CurrentDie(canvas, cursor);
        if (die != null)
            return ContentOf(canvas, die) ?? ToBox(DieAssembly.OutlineOf(die));
        return dies.Select(d => ContentOf(canvas, d)).Aggregate((BoundingBox?)null, Union) ?? ToBox(DieAssembly.CarrierOutline(dies));
    }

    /// <summary>The die of the selection, else the die under the cursor, else the die worked on last.</summary>
    public static ComponentGroup? CurrentDie(DesignCanvasViewModel canvas, (double X, double Y)? cursor)
    {
        var selected = canvas.SelectedComponent ?? canvas.Selection.SelectedComponents.FirstOrDefault();
        if (selected?.Component is ComponentGroup { IsDie: true } selectedDie)
            return selectedDie;
        if (DieAssembly.DieOf(selected?.Component) is { } partDie)
            return partDie;
        var dies = canvas.Dies.RootDies;
        if (cursor is { } point && dies.LastOrDefault(d => DieAssembly.OutlineOf(d).Contains(point.X, point.Y)) is { } underCursor)
            return underCursor;
        return canvas.Surface.LastWorkedDie is { } last && dies.Contains(last) ? last : null;
    }

    /// <summary>Bounds of the parts on <paramref name="die"/> and of its wires, or null for an empty die.</summary>
    public static BoundingBox? ContentOf(DesignCanvasViewModel canvas, ComponentGroup die)
    {
        BoundingBox? box = null;
        foreach (var part in die.ChildComponents)
        {
            var (x, y, width, height) = DiePlacement.FootprintOf(part);
            box = Union(box, new BoundingBox(x, y, x + width, y + height));
        }
        foreach (var wire in canvas.Connections.Select(c => c.Connection))
        {
            if (wire.RoutedPath is not { } path || !ReferenceEquals(ConnectionOwnership.DieOf(wire.StartPin), die))
                continue;
            foreach (var segment in path.Segments)
            {
                box = Union(box, Point(segment.StartPoint.X, segment.StartPoint.Y));
                box = Union(box, Point(segment.EndPoint.X, segment.EndPoint.Y));
            }
        }
        return box;
    }

    private static BoundingBox Point(double x, double y) => new(x, y, x, y);

    private static BoundingBox? ToBox(DieOutline? outline) =>
        outline is { } o ? new BoundingBox(o.X, o.Y, o.Right, o.Bottom) : null;

    private static BoundingBox? Union(BoundingBox? a, BoundingBox? b)
    {
        if (a is not { } first)
            return b;
        if (b is not { } second)
            return first;
        return new BoundingBox(Math.Min(first.MinX, second.MinX), Math.Min(first.MinY, second.MinY),
            Math.Max(first.MaxX, second.MaxX), Math.Max(first.MaxY, second.MaxY));
    }
}
