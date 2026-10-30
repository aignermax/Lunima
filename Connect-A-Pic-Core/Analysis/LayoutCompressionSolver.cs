namespace CAP_Core.Analysis;

/// <summary>Force calculation over private geometry bodies; never receives editor components.</summary>
internal sealed class LayoutCompressionSolver
{
    private const double AttractionStrength = 0.1;
    private const double RepulsionStrength = 50.0;
    private const double MinComponentSpacing = 20.0; // micrometers
    private const double DampingFactor = 0.7;
    private const double ConvergenceThreshold = 0.5; // micrometers
    private const int MaxIterations = 100;

    /// <summary>
    /// Compresses the layout by repositioning components to minimize chip area.
    /// Respects locked elements and maintains connectivity.
    /// </summary>
    /// <param name="components">List of components to compress.</param>
    /// <param name="connections">Waveguide connections between components.</param>
    /// <param name="progressCallback">Optional callback invoked periodically with iteration number and current positions.</param>
    /// <returns>New positions for each component (X, Y in micrometers).</returns>
    public Dictionary<LayoutCompressionBody, (double X, double Y)> CompressLayout(
        List<LayoutCompressionBody> components,
        IReadOnlyList<(int Start, int End)> connections,
        Action<int, Dictionary<LayoutCompressionBody, (double X, double Y)>>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (components.Count == 0)
            return new Dictionary<LayoutCompressionBody, (double X, double Y)>();

        // Separate locked and unlocked components
        var unlockedComponents = components.Where(c => !c.IsLocked).ToList();
        var lockedComponents = components.Where(c => c.IsLocked).ToList();

        if (unlockedComponents.Count == 0)
            return components.ToDictionary(c => c, c => (c.PhysicalX, c.PhysicalY));

        // Initialize velocity vectors
        var velocities = unlockedComponents.ToDictionary(
            c => c,
            c => (vx: 0.0, vy: 0.0));

        // Build connection graph
        var connectionGraph = BuildConnectionGraph(components, connections);

        // Run force-directed layout
        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var forces = CalculateForces(
                unlockedComponents,
                lockedComponents,
                connectionGraph, cancellationToken);

            // Update velocities and positions
            double maxDisplacement = 0;
            foreach (var comp in unlockedComponents)
            {
                var (fx, fy) = forces[comp];

                // Update velocity with damping
                velocities[comp] = (
                    vx: (velocities[comp].vx + fx) * DampingFactor,
                    vy: (velocities[comp].vy + fy) * DampingFactor
                );

                // Update position
                var (vx, vy) = velocities[comp];
                comp.PhysicalX += vx;
                comp.PhysicalY += vy;

                // Track convergence
                double displacement = Math.Sqrt(vx * vx + vy * vy);
                maxDisplacement = Math.Max(maxDisplacement, displacement);
            }

            // Report progress every 5 iterations for smooth animation
            if (progressCallback != null && iteration % 5 == 0)
            {
                var currentPositions = components.ToDictionary(
                    c => c,
                    c => (c.PhysicalX, c.PhysicalY));
                progressCallback(iteration, currentPositions);
            }

            // Check convergence
            if (maxDisplacement < ConvergenceThreshold)
                break;
        }

        // Center the layout to minimize bounding box
        CenterLayout(components);

        // Return final positions
        return components.ToDictionary(c => c, c => (c.PhysicalX, c.PhysicalY));
    }

    /// <summary>
    /// Builds a graph of which components are connected.
    /// </summary>
    private Dictionary<LayoutCompressionBody, List<LayoutCompressionBody>> BuildConnectionGraph(
        List<LayoutCompressionBody> components,
        IReadOnlyList<(int Start, int End)> connections)
    {
        var graph = components.ToDictionary(c => c, c => new List<LayoutCompressionBody>());

        foreach (var conn in connections)
        {
            var startComp = components[conn.Start];
            var endComp = components[conn.End];

            if (graph.ContainsKey(startComp) && graph.ContainsKey(endComp))
            {
                graph[startComp].Add(endComp);
                graph[endComp].Add(startComp);
            }
        }

        return graph;
    }

    /// <summary>
    /// Calculates attractive and repulsive forces on each unlocked component.
    /// </summary>
    private Dictionary<LayoutCompressionBody, (double fx, double fy)> CalculateForces(
        List<LayoutCompressionBody> unlockedComponents,
        List<LayoutCompressionBody> lockedComponents,
        Dictionary<LayoutCompressionBody, List<LayoutCompressionBody>> connectionGraph, CancellationToken cancellationToken)
    {
        var forces = unlockedComponents.ToDictionary(
            c => c,
            c => (fx: 0.0, fy: 0.0));

        // Attraction forces from connections
        foreach (var comp1 in unlockedComponents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var comp2 in connectionGraph[comp1])
            {
                var (fx, fy) = CalculateAttractionForce(comp1, comp2);
                forces[comp1] = (forces[comp1].fx + fx, forces[comp1].fy + fy);
            }
        }

        // Repulsion forces from all other components
        var allComponents = unlockedComponents.Concat(lockedComponents).ToList();
        foreach (var comp1 in unlockedComponents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var comp2 in allComponents)
            {
                if (comp1 == comp2) continue;

                var (fx, fy) = CalculateRepulsionForce(comp1, comp2);
                forces[comp1] = (forces[comp1].fx + fx, forces[comp1].fy + fy);
            }
        }

        return forces;
    }

    /// <summary>
    /// Calculates attractive force between connected components.
    /// Uses logarithmic attraction (standard in force-directed graph layouts)
    /// to pull components together while avoiding extreme forces.
    /// </summary>
    private (double fx, double fy) CalculateAttractionForce(
        LayoutCompressionBody comp1,
        LayoutCompressionBody comp2)
    {
        double dx = comp2.PhysicalX - comp1.PhysicalX;
        double dy = comp2.PhysicalY - comp1.PhysicalY;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance < 0.01)
            return (0, 0);

        // Logarithmic attraction force (standard in force-directed graph layouts)
        // F = k * log(d / d_min)
        // This provides strong attraction when far apart, weaker when close
        double minDistance = Math.Max(MinComponentSpacing, 10.0);
        double force = AttractionStrength * Math.Log(distance / minDistance);

        double fx = force * dx / distance;
        double fy = force * dy / distance;

        return (fx, fy);
    }

    /// <summary>
    /// Calculates repulsive force to maintain minimum spacing.
    /// </summary>
    private (double fx, double fy) CalculateRepulsionForce(
        LayoutCompressionBody comp1,
        LayoutCompressionBody comp2)
    {
        double dx = comp1.PhysicalX - comp2.PhysicalX;
        double dy = comp1.PhysicalY - comp2.PhysicalY;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance < 0.01)
        {
            // Components are too close - push in random direction
            var random = new Random();
            dx = random.NextDouble() - 0.5;
            dy = random.NextDouble() - 0.5;
            distance = Math.Sqrt(dx * dx + dy * dy);
        }

        // Calculate minimum distance based on component dimensions
        double minDist = MinComponentSpacing +
            (comp1.WidthMicrometers + comp2.WidthMicrometers) / 2;

        if (distance > minDist * 2)
            return (0, 0);

        // Inverse square law repulsion
        double force = RepulsionStrength * (minDist / distance) * (minDist / distance);
        double fx = force * dx / distance;
        double fy = force * dy / distance;

        return (fx, fy);
    }

    /// <summary>
    /// Centers the layout to minimize the bounding box.
    /// Only moves unlocked components.
    /// </summary>
    private void CenterLayout(List<LayoutCompressionBody> components)
    {
        if (components.Count == 0)
            return;

        double minX = components.Min(c => c.PhysicalX);
        double minY = components.Min(c => c.PhysicalY);

        // Shift all unlocked components to start near (0, 0) with small margin
        const double margin = 50.0;
        foreach (var comp in components)
        {
            if (!comp.IsLocked)
            {
                comp.PhysicalX -= minX - margin;
                comp.PhysicalY -= minY - margin;
            }
        }
    }

}
