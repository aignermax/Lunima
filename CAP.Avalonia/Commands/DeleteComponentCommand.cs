using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing.CrossingInsertion;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command for deleting a component from the canvas.
/// </summary>
public class DeleteComponentCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private ComponentViewModel _componentViewModel;
    private readonly Component _component;
    private readonly string? _templateName;
    private readonly string? _templatePdkSource;
    private readonly double _x;
    private readonly double _y;
    private readonly int? _laserWavelengthNm;
    private readonly double? _laserInputPower;

    /// <summary>
    /// Connections to restore on Undo. Crossing sub-connections are snapshotted as
    /// their pre-split ORIGINALS (#705): deleting the component dissolves its
    /// crossings, so re-adding the subs would resurrect ghost pins on a removed
    /// crossing component and duplicate the survivors the dissolution restored.
    /// </summary>
    private readonly List<WaveguideConnection> _deletedConnections = new();

    public DeleteComponentCommand(
        DesignCanvasViewModel canvas,
        ComponentViewModel componentViewModel)
    {
        _canvas = canvas;
        _componentViewModel = componentViewModel;
        _component = componentViewModel.Component;
        _templateName = componentViewModel.TemplateName;
        _templatePdkSource = componentViewModel.TemplatePdkSource;
        _x = componentViewModel.X;
        _y = componentViewModel.Y;
        // Wavelength/power live only on the ViewModel's LaserConfig — snapshot them so
        // undo does not silently reset the laser (#690; IsEnabled itself survives on
        // the core component, which is reused by Undo).
        _laserWavelengthNm = componentViewModel.LaserConfig?.WavelengthNm;
        _laserInputPower = componentViewModel.LaserConfig?.InputPower;
    }

    public string Description => $"Delete {_component.Identifier}";

    public void Execute()
    {
        // Don't delete locked components
        if (_component.IsLocked)
            return;

        // Store connections that will be deleted, normalized to pre-split originals
        // (two subs of the same crossing dedupe to one original).
        _deletedConnections.Clear();
        var crossing = _canvas.ConnectionManager.CrossingService;
        // A group's wires end at the pins of the components nested in it.
        var scope = new ComponentWireScope(_component);
        // Wires through routing-placed crossings leave whole, so undo restores whole wires.
        if (!ChainCrossingMaintenance.IsTemporaryCrossing(_component))
            _canvas.DissolveInsertedCrossingChains(scope.Touches);
        foreach (var connVm in _canvas.Connections.ToList())
        {
            if (scope.Touches(connVm.Connection))
            {
                var original = crossing?.ResolveToOriginal(connVm.Connection) ?? connVm.Connection;
                if (!_deletedConnections.Contains(original))
                    _deletedConnections.Add(original);
            }
        }

        _canvas.DeleteComponent(_componentViewModel);
    }

    public void Undo()
    {
        // Re-add the component (creates a new VM - update our reference for redo).
        // Keep the PDK source: a VM recreated without it would read as "built-in"
        // and slip through single-process enforcement on later copy/paste (#570).
        _component.PhysicalX = _x;
        _component.PhysicalY = _y;
        _componentViewModel = _canvas.AddComponent(_component, _templateName, _templatePdkSource);
        if (_componentViewModel.LaserConfig != null)
        {
            if (_laserWavelengthNm.HasValue)
                _componentViewModel.LaserConfig.WavelengthNm = _laserWavelengthNm.Value;
            if (_laserInputPower.HasValue)
                _componentViewModel.LaserConfig.InputPower = _laserInputPower.Value;
        }

        // Re-add connections. Dissolution may already have restored a survivor
        // original — both the manager and the VM list guard against duplicates.
        _canvas.RestoreConnections(_deletedConnections);

        // Recalculate routes asynchronously (grid is rebuilt inside RecalculateRoutesAsync)
        _ = _canvas.RecalculateRoutesAsync();

        // Invalidate simulation so power flow overlay updates with restored circuit
        _canvas.InvalidateSimulation();
    }
}
