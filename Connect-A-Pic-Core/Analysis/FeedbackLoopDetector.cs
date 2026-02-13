namespace CAP_Core.Analysis;

/// <summary>
/// Detects directed cycles (feedback loops) in a component graph using DFS.
/// </summary>
public class FeedbackLoopDetector
{
    /// <summary>
    /// Counts the number of distinct back-edges (feedback loops) in the graph.
    /// Each back-edge indicates one cycle in the directed graph.
    /// </summary>
    public int CountFeedbackLoops(ComponentGraph graph)
    {
        if (graph.Nodes.Count == 0) return 0;

        var visited = new HashSet<int>();
        var inStack = new HashSet<int>();
        int backEdgeCount = 0;

        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            if (!visited.Contains(i))
            {
                backEdgeCount += DfsCountBackEdges(i, graph, visited, inStack);
            }
        }

        return backEdgeCount;
    }

    private static int DfsCountBackEdges(
        int node,
        ComponentGraph graph,
        HashSet<int> visited,
        HashSet<int> inStack)
    {
        visited.Add(node);
        inStack.Add(node);
        int count = 0;

        if (graph.AdjacencyList.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (inStack.Contains(neighbor))
                {
                    count++;
                }
                else if (!visited.Contains(neighbor))
                {
                    count += DfsCountBackEdges(neighbor, graph, visited, inStack);
                }
            }
        }

        inStack.Remove(node);
        return count;
    }
}
