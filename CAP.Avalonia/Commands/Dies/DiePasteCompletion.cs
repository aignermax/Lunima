using CAP.Avalonia.Selection;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.DieSurface;
using CAP_Core.Components.Connections.Ownership;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;

namespace CAP.Avalonia.Commands.Dies;

/// <summary>
/// Finishes the paste of copied dies: dies are separate pieces of material and can never
/// share area, so a pasted die that would overlap another one goes to the free spot right
/// of the assembly; and a die's wires — live on the assembly canvas, not stored in the die —
/// are copied onto the pasted die's parts with their routes translated unchanged.
/// </summary>
public static class DiePasteCompletion
{
    /// <summary>
    /// Places every die among <paramref name="result"/>'s components free of the other dies and
    /// adds its copied wires to <paramref name="result"/> (so undo and redo take them along).
    /// </summary>
    public static void Complete(DesignCanvasViewModel canvas, PasteResult result)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(result);
        var pastedDies = result.Components.Where(c => c.Component is ComponentGroup { IsDie: true }).ToList();
        if (pastedDies.Count == 0)
            return;

        var pastedSet = new HashSet<Component>(pastedDies.Select(d => d.Component), ReferenceEqualityComparer.Instance);
        var placed = canvas.Dies.RootDies.Where(d => !pastedSet.Contains(d)).Select(DieAssembly.OutlineOf).ToList();
        foreach (var dieVm in pastedDies)
        {
            var die = (ComponentGroup)dieVm.Component;
            PlaceFree(dieVm, die, placed);
            placed.Add(DieAssembly.OutlineOf(die));
            if (OriginalOf(canvas, result, die) is { } original)
                CopyWires(canvas, original, die, result);
        }
        _ = canvas.Dies.ApplyScopeBoundsInBackground();
        canvas.NotifyDiesChanged();
    }

    private static void PlaceFree(ComponentViewModel dieVm, ComponentGroup die, List<DieOutline> others)
    {
        var outline = DieAssembly.OutlineOf(die);
        if (!DieAssembly.OverlapsAny(outline, others))
            return;
        var (x, y) = DieAssembly.NextDieOrigin(others);
        double dx = x - outline.X, dy = y - outline.Y;
        die.MoveGroup(dx, dy);
        dieVm.X = die.PhysicalX;
        dieVm.Y = die.PhysicalY;
        dieVm.NotifyDimensionsChanged();
    }

    private static ComponentGroup? OriginalOf(DesignCanvasViewModel canvas, PasteResult result, ComponentGroup clone)
    {
        var originalId = result.IdentifierMap.FirstOrDefault(p => p.Value == clone.Identifier).Key;
        return originalId == null ? null
            : canvas.Dies.RootDies.FirstOrDefault(d => d.Identifier == originalId && !ReferenceEquals(d, clone));
    }

    private static void CopyWires(DesignCanvasViewModel canvas, ComponentGroup original, ComponentGroup clone, PasteResult result)
    {
        var from = original.GetAllComponentsRecursive();
        var to = clone.GetAllComponentsRecursive();
        if (from.Count != to.Count)
            return;
        var counterpart = new Dictionary<Component, Component>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < from.Count; i++)
            counterpart[from[i]] = to[i];

        double dx = clone.PhysicalX - original.PhysicalX, dy = clone.PhysicalY - original.PhysicalY;
        var wires = canvas.Connections.Select(c => c.Connection)
            .Where(c => DieWireStyle.IsOnOneDie(c) && ReferenceEquals(ConnectionOwnership.DieOf(c.StartPin), original))
            .ToList();
        foreach (var wire in wires)
        {
            var start = PinOf(counterpart, wire.StartPin);
            var end = PinOf(counterpart, wire.EndPin);
            if (start == null || end == null || wire.RoutedPath is not { } path)
                continue;
            var settings = GroupWireHoming.Freeze(wire, original);
            var copy = canvas.ConnectPinsWithCachedRoute(start, end, path.TranslatedCopy(dx, dy));
            if (copy == null)
                continue;
            settings.ApplySettingsTo(copy.Connection);
            copy.Connection.UpdateLossFromPath();
            result.Connections.Add(copy);
        }
    }

    private static PhysicalPin? PinOf(Dictionary<Component, Component> counterpart, PhysicalPin pin) =>
        counterpart.TryGetValue(pin.ParentComponent, out var part)
            ? part.PhysicalPins.FirstOrDefault(p => p.Name == pin.Name)
            : null;
}
