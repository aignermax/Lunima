using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Routing;

namespace UnitTests.Helpers;

/// <summary>
/// Immutable snapshot of a single component's state for roundtrip comparison.
/// </summary>
public record ComponentSnapshot(
    string Identifier,
    string Type,
    double X,
    double Y,
    int Rotation,
    bool IsLocked);

/// <summary>
/// Immutable snapshot of a waveguide connection's routing path.
/// </summary>
public record WaveguideSnapshot(
    string StartComponent,
    string StartPin,
    string EndComponent,
    string EndPin,
    int SegmentCount,
    double FirstSegmentStartX,
    double FirstSegmentStartY);

/// <summary>
/// Complete snapshot of a design's state for comparison across export/import roundtrip.
/// </summary>
public class DesignSnapshot
{
    /// <summary>All component states captured at snapshot time.</summary>
    public List<ComponentSnapshot> Components { get; init; } = new();

    /// <summary>All waveguide connection states captured at snapshot time.</summary>
    public List<WaveguideSnapshot> Waveguides { get; init; } = new();
}

/// <summary>
/// Result of comparing two design snapshots with positional tolerance.
/// </summary>
public class SnapshotComparisonResult
{
    /// <summary>True if all components and waveguides match within tolerance.</summary>
    public bool IsMatch { get; set; } = true;

    /// <summary>All mismatch messages found during comparison.</summary>
    public List<string> Mismatches { get; } = new();

    /// <summary>Tolerance used for position comparisons (µm).</summary>
    public double PositionToleranceMicron { get; init; } = 0.01;
}

/// <summary>
/// Captures design state snapshots and compares them for roundtrip verification.
/// </summary>
public static class DesignSnapshotHelper
{
    /// <summary>
    /// Captures the current state of all components and connections on the canvas.
    /// </summary>
    public static DesignSnapshot Capture(DesignCanvasViewModel canvas)
    {
        var components = canvas.Components
            .Select(vm => CaptureComponent(vm.Component))
            .ToList();

        var waveguides = canvas.Connections
            .Where(vm => vm.Connection.RoutedPath?.Segments.Count > 0)
            .Select(vm => CaptureWaveguide(vm.Connection))
            .ToList();

        return new DesignSnapshot { Components = components, Waveguides = waveguides };
    }

    /// <summary>
    /// Compares two snapshots and returns a result with all mismatches.
    /// </summary>
    public static SnapshotComparisonResult Compare(
        DesignSnapshot original,
        DesignSnapshot reconstructed,
        double positionTolerance = 0.01)
    {
        var result = new SnapshotComparisonResult { PositionToleranceMicron = positionTolerance };

        CompareComponentCounts(original, reconstructed, result);
        CompareComponentPositions(original, reconstructed, result, positionTolerance);
        CompareWaveguideCounts(original, reconstructed, result);

        return result;
    }

    private static ComponentSnapshot CaptureComponent(Component comp)
    {
        return new ComponentSnapshot(
            comp.Identifier,
            comp.NazcaFunctionName ?? comp.HumanReadableName ?? comp.Identifier,
            comp.PhysicalX,
            comp.PhysicalY,
            (int)comp.Rotation90CounterClock,
            comp.IsLocked);
    }

    private static WaveguideSnapshot CaptureWaveguide(
        CAP_Core.Components.Connections.WaveguideConnection conn)
    {
        var segments = conn.RoutedPath?.Segments ?? new List<PathSegment>();
        var firstSeg = segments.Count > 0 ? segments[0] : null;

        return new WaveguideSnapshot(
            conn.StartPin.ParentComponent.Identifier,
            conn.StartPin.Name,
            conn.EndPin.ParentComponent.Identifier,
            conn.EndPin.Name,
            segments.Count,
            firstSeg?.StartPoint.X ?? 0,
            firstSeg?.StartPoint.Y ?? 0);
    }

    private static void CompareComponentCounts(
        DesignSnapshot original, DesignSnapshot reconstructed, SnapshotComparisonResult result)
    {
        if (original.Components.Count != reconstructed.Components.Count)
        {
            result.IsMatch = false;
            result.Mismatches.Add(
                $"Component count mismatch: expected {original.Components.Count}, " +
                $"got {reconstructed.Components.Count}");
        }
    }

    private static void CompareComponentPositions(
        DesignSnapshot original, DesignSnapshot reconstructed,
        SnapshotComparisonResult result, double tolerance)
    {
        foreach (var orig in original.Components)
        {
            var match = reconstructed.Components
                .FirstOrDefault(c => c.Identifier == orig.Identifier);

            if (match == null)
            {
                result.IsMatch = false;
                result.Mismatches.Add($"Component '{orig.Identifier}' not found in reconstructed design");
                continue;
            }

            var xDiff = Math.Abs(match.X - orig.X);
            var yDiff = Math.Abs(match.Y - orig.Y);

            if (xDiff > tolerance || yDiff > tolerance)
            {
                result.IsMatch = false;
                result.Mismatches.Add(
                    $"Component '{orig.Identifier}' position mismatch: " +
                    $"expected ({orig.X:F2},{orig.Y:F2}), got ({match.X:F2},{match.Y:F2})");
            }

            if (match.Rotation != orig.Rotation)
            {
                result.IsMatch = false;
                result.Mismatches.Add(
                    $"Component '{orig.Identifier}' rotation mismatch: " +
                    $"expected {orig.Rotation}, got {match.Rotation}");
            }

            if (match.IsLocked != orig.IsLocked)
            {
                result.IsMatch = false;
                result.Mismatches.Add(
                    $"Component '{orig.Identifier}' IsLocked mismatch: " +
                    $"expected {orig.IsLocked}, got {match.IsLocked}");
            }
        }
    }

    private static void CompareWaveguideCounts(
        DesignSnapshot original, DesignSnapshot reconstructed, SnapshotComparisonResult result)
    {
        if (original.Waveguides.Count != reconstructed.Waveguides.Count)
        {
            result.IsMatch = false;
            result.Mismatches.Add(
                $"Waveguide count mismatch: expected {original.Waveguides.Count}, " +
                $"got {reconstructed.Waveguides.Count}");
        }
    }
}
