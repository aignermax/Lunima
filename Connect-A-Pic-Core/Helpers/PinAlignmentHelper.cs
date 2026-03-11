using CAP_Core.Components;

namespace CAP_Core.Helpers;

/// <summary>
/// Detects when component pins align on the same X or Y coordinate.
/// Used to show visual alignment guides during component dragging.
/// </summary>
public class PinAlignmentHelper
{
    private const double DefaultAlignmentToleranceMicrometers = 1.0;

    /// <summary>
    /// Tolerance for considering two coordinates aligned (in micrometers).
    /// Default: 1.0µm (sub-wavelength precision for photonic layout).
    /// </summary>
    public double AlignmentToleranceMicrometers { get; set; } = DefaultAlignmentToleranceMicrometers;

    /// <summary>
    /// Finds all horizontal alignment lines (pins with same Y coordinate).
    /// </summary>
    /// <param name="draggingComponent">The component being dragged.</param>
    /// <param name="otherComponents">All other components on the canvas.</param>
    /// <returns>List of Y coordinates where horizontal alignment occurs.</returns>
    public List<HorizontalAlignment> FindHorizontalAlignments(
        Component draggingComponent,
        IEnumerable<Component> otherComponents)
    {
        if (draggingComponent == null)
            throw new ArgumentNullException(nameof(draggingComponent));

        var alignments = new List<HorizontalAlignment>();
        var draggingPins = draggingComponent.PhysicalPins;

        foreach (var otherComp in otherComponents)
        {
            if (otherComp == draggingComponent) continue;

            foreach (var draggingPin in draggingPins)
            {
                var (_, draggingY) = draggingPin.GetAbsolutePosition();

                foreach (var otherPin in otherComp.PhysicalPins)
                {
                    var (_, otherY) = otherPin.GetAbsolutePosition();

                    if (Math.Abs(draggingY - otherY) <= AlignmentToleranceMicrometers)
                    {
                        alignments.Add(new HorizontalAlignment(
                            draggingY,
                            draggingPin,
                            otherPin));
                    }
                }
            }
        }

        return alignments;
    }

    /// <summary>
    /// Finds all vertical alignment lines (pins with same X coordinate).
    /// </summary>
    /// <param name="draggingComponent">The component being dragged.</param>
    /// <param name="otherComponents">All other components on the canvas.</param>
    /// <returns>List of X coordinates where vertical alignment occurs.</returns>
    public List<VerticalAlignment> FindVerticalAlignments(
        Component draggingComponent,
        IEnumerable<Component> otherComponents)
    {
        if (draggingComponent == null)
            throw new ArgumentNullException(nameof(draggingComponent));

        var alignments = new List<VerticalAlignment>();
        var draggingPins = draggingComponent.PhysicalPins;

        foreach (var otherComp in otherComponents)
        {
            if (otherComp == draggingComponent) continue;

            foreach (var draggingPin in draggingPins)
            {
                var (draggingX, _) = draggingPin.GetAbsolutePosition();

                foreach (var otherPin in otherComp.PhysicalPins)
                {
                    var (otherX, _) = otherPin.GetAbsolutePosition();

                    if (Math.Abs(draggingX - otherX) <= AlignmentToleranceMicrometers)
                    {
                        alignments.Add(new VerticalAlignment(
                            draggingX,
                            draggingPin,
                            otherPin));
                    }
                }
            }
        }

        return alignments;
    }

    /// <summary>
    /// Finds both horizontal and vertical alignments in one pass.
    /// More efficient than calling both methods separately.
    /// </summary>
    /// <param name="draggingComponent">The component being dragged.</param>
    /// <param name="otherComponents">All other components on the canvas.</param>
    /// <returns>Tuple containing horizontal and vertical alignments.</returns>
    public (List<HorizontalAlignment> horizontal, List<VerticalAlignment> vertical) FindAllAlignments(
        Component draggingComponent,
        IEnumerable<Component> otherComponents)
    {
        if (draggingComponent == null)
            throw new ArgumentNullException(nameof(draggingComponent));

        var horizontalAlignments = new List<HorizontalAlignment>();
        var verticalAlignments = new List<VerticalAlignment>();
        var draggingPins = draggingComponent.PhysicalPins;

        foreach (var otherComp in otherComponents)
        {
            if (otherComp == draggingComponent) continue;

            foreach (var draggingPin in draggingPins)
            {
                var (draggingX, draggingY) = draggingPin.GetAbsolutePosition();

                foreach (var otherPin in otherComp.PhysicalPins)
                {
                    // Skip if pins don't point at each other (must be ~180° apart)
                    if (!ArePinsOpposing(draggingPin, otherPin))
                        continue;

                    var (otherX, otherY) = otherPin.GetAbsolutePosition();

                    // Check horizontal alignment (same Y)
                    if (Math.Abs(draggingY - otherY) <= AlignmentToleranceMicrometers)
                    {
                        horizontalAlignments.Add(new HorizontalAlignment(
                            otherY,  // Use target Y for snapping
                            draggingPin,
                            otherPin));
                    }

                    // Check vertical alignment (same X)
                    if (Math.Abs(draggingX - otherX) <= AlignmentToleranceMicrometers)
                    {
                        verticalAlignments.Add(new VerticalAlignment(
                            otherX,  // Use target X for snapping
                            draggingPin,
                            otherPin));
                    }
                }
            }
        }

        return (horizontalAlignments, verticalAlignments);
    }

    /// <summary>
    /// Checks if two pins are facing each other (opposing directions ~180° apart)
    /// AND if they geometrically point at each other's locations.
    /// This ensures we only snap when the pin actually "sees" the other component.
    /// </summary>
    private bool ArePinsOpposing(PhysicalPin pin1, PhysicalPin pin2)
    {
        // Get absolute angles (including component rotation)
        double angle1 = pin1.GetAbsoluteAngle();
        double angle2 = pin2.GetAbsoluteAngle();

        // Calculate angular difference
        double diff = Math.Abs(angle1 - angle2);
        if (diff > 180)
            diff = 360 - diff;

        // Pins must be within 10° of being 180° apart
        if (Math.Abs(diff - 180) > 10)
            return false;

        // Now check if pin1 actually points TOWARDS pin2's location
        var (x1, y1) = pin1.GetAbsolutePosition();
        var (x2, y2) = pin2.GetAbsolutePosition();

        // Calculate the angle from pin1 to pin2
        double dx = x2 - x1;
        double dy = y2 - y1;
        double angleToTarget = Math.Atan2(dy, dx) * 180.0 / Math.PI;

        // Normalize to 0-360 range
        angleToTarget = ((angleToTarget % 360) + 360) % 360;

        // Check if pin1's direction matches the direction to pin2 (within tolerance)
        double directionDiff = Math.Abs(angle1 - angleToTarget);
        if (directionDiff > 180)
            directionDiff = 360 - directionDiff;

        // Pin must point within 30° of the target (allows some angular tolerance)
        return directionDiff <= 30;
    }

    /// <summary>
    /// Calculates snap delta to align the dragging component's pins with nearby opposing pins.
    /// Returns the offset to apply to the component position to achieve perfect alignment.
    /// </summary>
    /// <param name="draggingComponent">The component being dragged.</param>
    /// <param name="otherComponents">All other components on the canvas.</param>
    /// <param name="snapToleranceMicrometers">Maximum distance to trigger snapping (default 5µm).</param>
    /// <returns>Tuple of (deltaX, deltaY) to apply for snapping, or (0, 0) if no snap.</returns>
    public (double deltaX, double deltaY) CalculateSnapDelta(
        Component draggingComponent,
        IEnumerable<Component> otherComponents,
        double snapToleranceMicrometers = 5.0)
    {
        var (horizontal, vertical) = FindAllAlignments(draggingComponent, otherComponents);

        double snapDeltaX = 0;
        double snapDeltaY = 0;

        // Snap to closest horizontal alignment (Y coordinate)
        if (horizontal.Count > 0)
        {
            var closest = horizontal
                .OrderBy(a => Math.Abs(a.YCoordinate - a.DraggingPin.GetAbsolutePosition().y))
                .First();

            double diff = closest.YCoordinate - closest.DraggingPin.GetAbsolutePosition().y;
            if (Math.Abs(diff) <= snapToleranceMicrometers)
                snapDeltaY = diff;
        }

        // Snap to closest vertical alignment (X coordinate)
        if (vertical.Count > 0)
        {
            var closest = vertical
                .OrderBy(a => Math.Abs(a.XCoordinate - a.DraggingPin.GetAbsolutePosition().x))
                .First();

            double diff = closest.XCoordinate - closest.DraggingPin.GetAbsolutePosition().x;
            if (Math.Abs(diff) <= snapToleranceMicrometers)
                snapDeltaX = diff;
        }

        return (snapDeltaX, snapDeltaY);
    }
}

/// <summary>
/// Represents a horizontal alignment line (same Y coordinate).
/// </summary>
public record HorizontalAlignment(
    double YCoordinate,
    PhysicalPin DraggingPin,
    PhysicalPin AlignedPin);

/// <summary>
/// Represents a vertical alignment line (same X coordinate).
/// </summary>
public record VerticalAlignment(
    double XCoordinate,
    PhysicalPin DraggingPin,
    PhysicalPin AlignedPin);
