namespace CAP_Core.Analysis;

/// <summary>
/// Calculates network depth and path lengths from inputs to outputs using BFS.
/// </summary>
public class NetworkDepthCalculator
{
    /// <summary>
    /// Finds all path lengths from input nodes to output nodes.
    /// Returns the length of each discovered path (in component hops).
    /// </summary>
    public List<int> FindAllPathLengths(ComponentGraph graph)
    {
        var pathLengths = new List<int>();
        var outputSet = new HashSet<int>(graph.OutputNodeIndices);

        foreach (var inputIndex in graph.InputNodeIndices)
        {
            var paths = FindPathsFromSource(inputIndex, outputSet, graph);
            pathLengths.AddRange(paths);
        }

        return pathLengths;
    }

    /// <summary>
    /// Calculates the network depth (longest path from any input to any output).
    /// Returns zero if no input-to-output path exists.
    /// </summary>
    public int CalculateNetworkDepth(ComponentGraph graph)
    {
        var pathLengths = FindAllPathLengths(graph);
        return pathLengths.Count > 0 ? pathLengths.Max() : 0;
    }

    private static List<int> FindPathsFromSource(
        int sourceIndex,
        HashSet<int> outputSet,
        ComponentGraph graph)
    {
        var results = new List<int>();
        var visited = new HashSet<int>();
        var queue = new Queue<(int Node, int Depth)>();
        queue.Enqueue((sourceIndex, 0));
        visited.Add(sourceIndex);

        while (queue.Count > 0)
        {
            var (node, depth) = queue.Dequeue();

            if (outputSet.Contains(node))
            {
                results.Add(depth);
            }

            if (!graph.AdjacencyList.TryGetValue(node, out var neighbors))
                continue;

            foreach (var neighbor in neighbors)
            {
                if (visited.Contains(neighbor)) continue;
                visited.Add(neighbor);
                queue.Enqueue((neighbor, depth + 1));
            }
        }

        return results;
    }
}
