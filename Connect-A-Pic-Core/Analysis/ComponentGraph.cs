using CAP_Core.Components;

namespace CAP_Core.Analysis;

/// <summary>
/// Directed graph of component connections extracted from the grid.
/// Nodes are components, edges represent optical connections between them.
/// </summary>
public class ComponentGraph
{
    /// <summary>
    /// All components in the graph as indexed nodes.
    /// </summary>
    public IReadOnlyList<Component> Nodes { get; }

    /// <summary>
    /// Adjacency list: for each node index, the set of node indices it connects to.
    /// </summary>
    public IReadOnlyDictionary<int, HashSet<int>> AdjacencyList { get; }

    /// <summary>
    /// Indices of components that are connected to external inputs.
    /// </summary>
    public IReadOnlyList<int> InputNodeIndices { get; }

    /// <summary>
    /// Indices of components that are connected to external outputs.
    /// </summary>
    public IReadOnlyList<int> OutputNodeIndices { get; }

    /// <summary>
    /// Creates a new component graph.
    /// </summary>
    public ComponentGraph(
        IReadOnlyList<Component> nodes,
        IReadOnlyDictionary<int, HashSet<int>> adjacencyList,
        IReadOnlyList<int> inputNodeIndices,
        IReadOnlyList<int> outputNodeIndices)
    {
        Nodes = nodes;
        AdjacencyList = adjacencyList;
        InputNodeIndices = inputNodeIndices;
        OutputNodeIndices = outputNodeIndices;
    }
}
