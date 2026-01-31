using CAP.Avalonia.ViewModels;
using CAP_Core.Components;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command for rotating a component 90° counter-clockwise.
/// </summary>
public class RotateComponentCommand : IUndoableCommand
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly ComponentViewModel _component;

    public RotateComponentCommand(DesignCanvasViewModel canvas, ComponentViewModel component)
    {
        _canvas = canvas;
        _component = component;
    }

    public string Description => $"Rotate {_component.Name}";

    public void Execute()
    {
        RotateComponent90();
    }

    public void Undo()
    {
        // Rotate 3 times to undo (270° = -90°)
        RotateComponent90();
        RotateComponent90();
        RotateComponent90();
    }

    private void RotateComponent90()
    {
        var comp = _component.Component;
        var width = comp.WidthMicrometers;
        var height = comp.HeightMicrometers;

        // Rotate each physical pin's offset around the component center
        // Pin angles stay relative to the component - GetAbsoluteAngle() adds RotationDegrees
        foreach (var pin in comp.PhysicalPins)
        {
            // Rotate offset 90° counter-clockwise around center
            // Center is at (width/2, height/2)
            var cx = width / 2;
            var cy = height / 2;

            // Translate to origin
            var x = pin.OffsetXMicrometers - cx;
            var y = pin.OffsetYMicrometers - cy;

            // Rotate 90° counter-clockwise: (x, y) -> (-y, x)
            var newX = -y;
            var newY = x;

            // Translate back (but to new center after dimension swap)
            pin.OffsetXMicrometers = newX + cy; // cy becomes new cx
            pin.OffsetYMicrometers = newY + cx; // cx becomes new cy

            // NOTE: Pin angles are stored relative to the component.
            // GetAbsoluteAngle() adds component.RotationDegrees to get world-space angle.
            // Do NOT modify pin.AngleDegrees here.
        }

        // Swap dimensions
        comp.WidthMicrometers = height;
        comp.HeightMicrometers = width;

        // Update the component's discrete rotation and RotationDegrees
        comp.RotateBy90CounterClockwise();

        // Notify the view model of dimension changes
        _component.NotifyDimensionsChanged();

        // Update any connected waveguides
        foreach (var conn in _canvas.Connections)
        {
            if (conn.Connection.StartPin.ParentComponent == comp ||
                conn.Connection.EndPin.ParentComponent == comp)
            {
                conn.NotifyPathChanged();
            }
        }
    }
}
