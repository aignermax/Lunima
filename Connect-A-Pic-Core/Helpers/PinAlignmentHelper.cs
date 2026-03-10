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
                    var (otherX, otherY) = otherPin.GetAbsolutePosition();

                    // Check horizontal alignment (same Y)
                    if (Math.Abs(draggingY - otherY) <= AlignmentToleranceMicrometers)
                    {
                        horizontalAlignments.Add(new HorizontalAlignment(
                            draggingY,
                            draggingPin,
                            otherPin));
                    }

                    // Check vertical alignment (same X)
                    if (Math.Abs(draggingX - otherX) <= AlignmentToleranceMicrometers)
                    {
                        verticalAlignments.Add(new VerticalAlignment(
                            draggingX,
                            draggingPin,
                            otherPin));
                    }
                }
            }
        }

        return (horizontalAlignments, verticalAlignments);
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
