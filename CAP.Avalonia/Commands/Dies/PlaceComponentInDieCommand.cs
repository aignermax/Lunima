using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;

namespace CAP.Avalonia.Commands.Dies;

/// <summary>
/// Drops a library part onto a die while looking at the whole assembly: the part becomes a
/// child of the die (it belongs to that die's process and moves with it), lands on the
/// nearest free spot inside the outline, and its pins join the die's interface so an edge
/// coupler's facet can be linked to the neighbouring die right away.
/// </summary>
public sealed class PlaceComponentInDieCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ComponentGroup _die;
    private readonly string _templateName;

    private PlaceComponentInDieCommand(DesignCanvasViewModel canvas, ComponentGroup die, Component component, string templateName)
    {
        _canvas = canvas;
        _die = die;
        PlacedComponent = component;
        _templateName = templateName;
    }

    /// <summary>
    /// Creates the command for <paramref name="template"/> centered at (<paramref name="x"/>,
    /// <paramref name="y"/>), or null when the die has no free room for it.
    /// </summary>
    public static PlaceComponentInDieCommand? TryCreate(
        DesignCanvasViewModel canvas, ComponentGroup die, ComponentTemplate template, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(die);
        ArgumentNullException.ThrowIfNull(template);

        var spot = DiePlacement.FindFreeSpot(die, template.WidthMicrometers, template.HeightMicrometers,
            x - template.WidthMicrometers / 2, y - template.HeightMicrometers / 2);
        if (spot == null)
            return null;

        var component = ComponentTemplates.CreateFromTemplate(template, spot.Value.X, spot.Value.Y);
        if (EdgeCouplerFacet.IsEdgeCoupler(component)
            && DieEdgeSnapper.Plan(component, DieAssembly.OutlineOf(die), die.Die!.EdgeZoneMicrometers) is { } snap)
            DieEdgeSnapper.Apply(component, snap);
        return new PlaceComponentInDieCommand(canvas, die, component, template.Name);
    }

    /// <summary>The part placed by this command.</summary>
    public Component PlacedComponent { get; }

    /// <summary>The die the part is placed into.</summary>
    public ComponentGroup Die => _die;

    /// <inheritdoc/>
    public string Description => $"Place {_templateName} on {_die.GroupName}";

    /// <inheritdoc/>
    public void Execute()
    {
        if (!_die.ChildComponents.Contains(PlacedComponent))
            _die.AddChild(PlacedComponent);
        AfterChange();
    }

    /// <inheritdoc/>
    public void Undo()
    {
        _die.RemoveChild(PlacedComponent);
        AfterChange();
    }

    private void AfterChange()
    {
        _canvas.RefreshDieInterfaces();
        _canvas.InvalidateSimulation();
        _canvas.NotifyDiesChanged();
    }
}
