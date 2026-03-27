using System.Numerics;
using CAP_Core.Routing;

namespace CAP_Core.Components.Core;

/// <summary>
/// Represents a waveguide path that is frozen (fixed geometry).
/// Unlike regular RoutedPath, these don't recalculate during group moves.
/// The path geometry is stored in absolute coordinates and translated when the group moves.
/// </summary>
public class FrozenWaveguidePath : ICloneable
{
    /// <summary>
    /// The routed path segments with fixed geometry.
    /// </summary>
    public RoutedPath Path { get; set; }

    /// <summary>
    /// Physical pin where this frozen path starts.
    /// </summary>
    public PhysicalPin StartPin { get; set; }

    /// <summary>
    /// Physical pin where this frozen path ends.
    /// </summary>
    public PhysicalPin EndPin { get; set; }

    /// <summary>
    /// Unique identifier for this frozen path.
    /// </summary>
    public Guid PathId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Propagation loss in dB per centimeter.
    /// Default matches the standard waveguide loss used in WaveguideConnection.
    /// </summary>
    public double PropagationLossDbPerCm { get; set; } = 2.0;

    /// <summary>
    /// Amplitude transmission coefficient accounting for propagation loss.
    /// Returns Complex.One when no path is available (conservative, no loss assumed).
    /// Formula: amplitude = 10^(-loss_dB / 20), where loss_dB = PropagationLossDbPerCm * length_cm.
    /// </summary>
    public Complex TransmissionCoefficient
    {
        get
        {
            if (Path?.Segments == null || Path.Segments.Count == 0)
                return Complex.One;

            double lengthMicrometers = Path.TotalLengthMicrometers;
            double lengthCm = lengthMicrometers / 10_000.0;
            double lossDb = PropagationLossDbPerCm * lengthCm;
            double amplitude = Math.Pow(10.0, -lossDb / 20.0);
            return new Complex(amplitude, 0);
        }
    }

    /// <summary>
    /// Translates all segments in the path by the specified delta.
    /// Used when moving the containing ComponentGroup.
    /// </summary>
    /// <param name="deltaX">X offset in micrometers.</param>
    /// <param name="deltaY">Y offset in micrometers.</param>
    public void TranslateBy(double deltaX, double deltaY)
    {
        if (Path?.Segments == null) return;

        foreach (var segment in Path.Segments)
        {
            segment.StartPoint = (
                segment.StartPoint.X + deltaX,
                segment.StartPoint.Y + deltaY
            );
            segment.EndPoint = (
                segment.EndPoint.X + deltaX,
                segment.EndPoint.Y + deltaY
            );

            // If it's a bend segment, translate the center point as well
            if (segment is BendSegment bend)
            {
                bend.Center = (
                    bend.Center.X + deltaX,
                    bend.Center.Y + deltaY
                );
            }
        }
    }

    /// <summary>
    /// Creates a clone of this frozen path with a new ID.
    /// </summary>
    public object Clone()
    {
        var clonedPath = new RoutedPath
        {
            IsBlockedFallback = Path.IsBlockedFallback,
            IsInvalidGeometry = Path.IsInvalidGeometry
        };

        // Deep clone all segments
        foreach (var segment in Path.Segments)
        {
            if (segment is BendSegment bend)
            {
                clonedPath.Segments.Add(new BendSegment(
                    bend.Center.X,
                    bend.Center.Y,
                    bend.RadiusMicrometers,
                    bend.StartAngleDegrees,
                    bend.SweepAngleDegrees
                ));
            }
            else if (segment is StraightSegment straight)
            {
                clonedPath.Segments.Add(new StraightSegment(
                    straight.StartPoint.X,
                    straight.StartPoint.Y,
                    straight.EndPoint.X,
                    straight.EndPoint.Y,
                    straight.StartAngleDegrees
                ));
            }
        }

        return new FrozenWaveguidePath
        {
            Path = clonedPath,
            PathId = Guid.NewGuid(),
            // StartPin and EndPin references must be updated after cloning by the ComponentGroup
        };
    }
}
