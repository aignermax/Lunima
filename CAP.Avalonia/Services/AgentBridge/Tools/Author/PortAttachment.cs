using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Author;

/// <summary>A solved port-relative pose: top-left position and counter-clockwise quarter turns.</summary>
public sealed record AttachedPose(double X, double Y, int QuarterTurns);

/// <summary>
/// Port-relative placement (gdsfactory's <c>connect</c>): finds the rotation and top-left position
/// that put a new component's pin facing a target pin, <c>gap</c> µm in front of it — so an agent
/// never computes coordinates by hand. Pin angles point out of the component; in the canvas'
/// y-down frame an angle θ points along (cos θ, sin θ).
/// </summary>
public static class PortAttachment
{
    private const double AngleTolerance = 0.5;
    private const int QuarterTurn = 90;

    /// <summary>Solves the pose of <paramref name="template"/> so <paramref name="ownPin"/> faces <paramref name="target"/>.</summary>
    public static AttachedPose? Solve(DesignCanvasViewModel canvas, ComponentTemplate template, string ownPin,
        PhysicalPin target, double gap, out string? error)
    {
        error = null;
        var (tx, ty) = target.GetAbsolutePosition();
        var theta = target.GetAbsoluteAngle();
        var facing = (theta + 180) % 360;
        var radians = theta * Math.PI / 180;

        for (var turns = 0; turns < 4; turns++)
        {
            var probe = PlaceComponentCommand.CreateExact(canvas, template, 0, 0, turns).PlacedComponent!;
            var pin = probe.PhysicalPins.FirstOrDefault(p => string.Equals(p.Name, ownPin, StringComparison.OrdinalIgnoreCase));
            if (pin == null)
            {
                error = $"'{template.Name}' has no pin '{ownPin}'. Pins: {string.Join(", ", probe.PhysicalPins.Select(p => p.Name))}.";
                return null;
            }
            if (AngleDistance(pin.GetAbsoluteAngle(), facing) > AngleTolerance)
                continue;

            var (px, py) = pin.GetAbsolutePosition();
            var anchorX = tx + gap * Math.Cos(radians);
            var anchorY = ty + gap * Math.Sin(radians);
            return new AttachedPose(Math.Round(anchorX - px, 4), Math.Round(anchorY - py, 4), turns);
        }

        error = $"No quarter-turn rotation lets '{template.Name}.{ownPin}' face the target pin (angle {theta}°).";
        return null;
    }

    /// <summary>The rotation in degrees for <paramref name="quarterTurns"/>.</summary>
    public static int Degrees(int quarterTurns) => quarterTurns * QuarterTurn;

    private static double AngleDistance(double a, double b)
    {
        var d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }
}
