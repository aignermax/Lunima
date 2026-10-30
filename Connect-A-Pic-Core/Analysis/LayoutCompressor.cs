using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;

namespace CAP_Core.Analysis;

/// <summary>Compatibility facade for callers that intentionally apply compression in place.</summary>
public class LayoutCompressor
{
    public Dictionary<Component, (double X, double Y)> CompressLayout(
        List<Component> components, List<WaveguideConnection> connections,
        Action<int, Dictionary<Component, (double X, double Y)>>? progressCallback = null)
    {
        var bodies = components.Select(LayoutCompressionBody.Capture).ToList();
        var edges = LayoutCompressionSnapshot.CaptureEdges(components, connections);
        var result = new LayoutCompressionSolver().CompressLayout(bodies, edges, (iteration, positions) =>
        {
            Apply(components, bodies);
            progressCallback?.Invoke(iteration, components.ToDictionary(c => c, c => (c.PhysicalX, c.PhysicalY)));
            // Preserve the legacy callback's ability to adjust geometry before the next iteration.
            for (var i = 0; i < components.Count; i++) bodies[i].ReadFrom(components[i]);
        });
        Apply(components, bodies);
        return components.Select((component, index) => (component, position: result[bodies[index]]))
            .ToDictionary(pair => pair.component, pair => pair.position);
    }

    private static void Apply(List<Component> components, List<LayoutCompressionBody> bodies)
    {
        for (var i = 0; i < components.Count; i++)
        {
            components[i].PhysicalX = bodies[i].PhysicalX;
            components[i].PhysicalY = bodies[i].PhysicalY;
        }
    }

    /// <summary>
    /// Calculates the bounding box of the current layout.
    /// </summary>
    /// <returns>Tuple of (minX, minY, maxX, maxY, width, height, area).</returns>
    public (double minX, double minY, double maxX, double maxY, double width, double height, double area)
        CalculateBoundingBox(List<Component> components)
    {
        if (components.Count == 0)
            return (0, 0, 0, 0, 0, 0, 0);

        double minX = components.Min(c => c.PhysicalX);
        double minY = components.Min(c => c.PhysicalY);
        double maxX = components.Max(c => c.PhysicalX + c.WidthMicrometers);
        double maxY = components.Max(c => c.PhysicalY + c.HeightMicrometers);

        double width = maxX - minX;
        double height = maxY - minY;
        double area = width * height;

        return (minX, minY, maxX, maxY, width, height, area);
    }
}
