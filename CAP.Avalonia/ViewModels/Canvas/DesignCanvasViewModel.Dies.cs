using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>Die (chiplet) half of <see cref="DesignCanvasViewModel"/>.</summary>
public partial class DesignCanvasViewModel
{
    /// <summary>
    /// Raised after the set of dies, their outlines, processes or PDKs changed, so panels
    /// that list or scope by dies (Chiplets panel, die properties, library) can refresh.
    /// </summary>
    public event Action? DiesChanged;

    /// <summary>Notifies <see cref="DiesChanged"/> listeners and repaints the canvas.</summary>
    public void NotifyDiesChanged()
    {
        DiesChanged?.Invoke();
        RepaintRequested?.Invoke();
    }

    /// <summary>
    /// Brings every root die's external pins in line with its content (see
    /// <see cref="DiePinExposure"/>) and re-registers the pins the assembly can wire to.
    /// </summary>
    public void RefreshDieInterfaces()
    {
        if (IsInGroupEditMode)
            return;

        var wiredOutside = ConnectionManager.Connections
            .SelectMany(c => new[] { c.StartPin, c.EndPin })
            .ToHashSet();
        foreach (var vm in Components.Where(c => c.Component is ComponentGroup { IsDie: true }).ToList())
        {
            var die = (ComponentGroup)vm.Component;
            DiePinExposure.Refresh(die, wiredOutside);
            ResyncGroupPinViewModels(vm, die);
            vm.NotifyDimensionsChanged();
        }
    }

    /// <summary>Replaces the pin view models of a group with one per current external pin.</summary>
    private void ResyncGroupPinViewModels(ComponentViewModel vm, ComponentGroup group)
    {
        foreach (var stale in AllPins.Where(p => p.ParentComponentViewModel == vm).ToList())
            AllPins.Remove(stale);
        foreach (var pin in group.PhysicalPins)
            AllPins.Add(new PinViewModel(pin, vm));
        foreach (var groupPin in group.ExternalPins)
            AllPins.Add(new PinViewModel(groupPin.InternalPin, vm));
    }
}
