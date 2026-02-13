using CAP_Core.Components;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.Tiles;

namespace CAP_Core.Analysis;

/// <summary>
/// Builds a directed component graph from grid topology.
/// Extracts adjacency, input nodes, and output nodes.
/// </summary>
public class ComponentGraphBuilder
{
    private readonly ITileManager _tileManager;
    private readonly IComponentRelationshipManager _relationshipManager;
    private readonly IExternalPortManager _externalPortManager;

    /// <summary>
    /// Creates a new graph builder with required grid dependencies.
    /// </summary>
    public ComponentGraphBuilder(
        ITileManager tileManager,
        IComponentRelationshipManager relationshipManager,
        IExternalPortManager externalPortManager)
    {
        _tileManager = tileManager;
        _relationshipManager = relationshipManager;
        _externalPortManager = externalPortManager;
    }

    /// <summary>
    /// Builds a directed component graph from the current grid state.
    /// </summary>
    public ComponentGraph Build()
    {
        var components = _tileManager.GetAllComponents();
        var componentIndex = BuildComponentIndex(components);
        var adjacency = BuildAdjacencyList(components, componentIndex);
        var inputIndices = FindInputNodeIndices(componentIndex);
        var outputIndices = FindOutputNodeIndices(componentIndex);

        return new ComponentGraph(components, adjacency, inputIndices, outputIndices);
    }

    private static Dictionary<Component, int> BuildComponentIndex(
        List<Component> components)
    {
        var index = new Dictionary<Component, int>();
        for (int i = 0; i < components.Count; i++)
        {
            index[components[i]] = i;
        }
        return index;
    }

    private Dictionary<int, HashSet<int>> BuildAdjacencyList(
        List<Component> components,
        Dictionary<Component, int> componentIndex)
    {
        var adjacency = new Dictionary<int, HashSet<int>>();
        for (int i = 0; i < components.Count; i++)
        {
            adjacency[i] = new HashSet<int>();
        }

        foreach (var component in components)
        {
            var neighbors = _relationshipManager
                .GetConnectedNeighborsOfComponent(component);
            var sourceIndex = componentIndex[component];

            foreach (var connection in neighbors)
            {
                var neighborComponent = connection.Child.Component;
                if (neighborComponent == null) continue;
                if (!componentIndex.TryGetValue(neighborComponent, out var targetIndex))
                    continue;

                adjacency[sourceIndex].Add(targetIndex);
            }
        }

        return adjacency;
    }

    private List<int> FindInputNodeIndices(
        Dictionary<Component, int> componentIndex)
    {
        var indices = new List<int>();
        foreach (var port in _externalPortManager.ExternalPorts)
        {
            if (port is not ExternalInput) continue;
            var component = GetComponentAtPort(port);
            if (component != null && componentIndex.TryGetValue(component, out var idx))
            {
                if (!indices.Contains(idx))
                    indices.Add(idx);
            }
        }
        return indices;
    }

    private List<int> FindOutputNodeIndices(
        Dictionary<Component, int> componentIndex)
    {
        var indices = new List<int>();
        foreach (var port in _externalPortManager.ExternalPorts)
        {
            if (port is not ExternalOutput) continue;
            var component = GetComponentAtPort(port);
            if (component != null && componentIndex.TryGetValue(component, out var idx))
            {
                if (!indices.Contains(idx))
                    indices.Add(idx);
            }
        }
        return indices;
    }

    private Component? GetComponentAtPort(ExternalPort port)
    {
        var x = port.IsLeftPort ? 0 : _tileManager.Width - 1;
        var y = port.TilePositionY;
        if (!_tileManager.IsCoordinatesInGrid(x, y)) return null;
        return _tileManager.Tiles[x, y]?.Component;
    }
}
