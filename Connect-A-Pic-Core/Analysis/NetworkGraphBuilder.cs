using CAP_Core.Components;

namespace CAP_Core.Analysis
{
    /// <summary>
    /// Builds a directed graph representation from components and waveguide connections.
    /// Each component is a node; each waveguide connection is a directed edge.
    /// </summary>
    public static class NetworkGraphBuilder
    {
        /// <summary>
        /// Builds an adjacency list from waveguide connections.
        /// Key = source component, Value = list of (target component, connection).
        /// </summary>
        /// <param name="connections">All waveguide connections in the network.</param>
        /// <returns>Adjacency list mapping each component to its outgoing edges.</returns>
        public static Dictionary<Component, List<(Component Target, WaveguideConnection Connection)>>
            BuildAdjacencyList(IReadOnlyList<WaveguideConnection> connections)
        {
            var adjacency = new Dictionary<Component, List<(Component, WaveguideConnection)>>();

            foreach (var connection in connections)
            {
                if (connection.StartPin?.ParentComponent == null ||
                    connection.EndPin?.ParentComponent == null)
                {
                    continue;
                }

                var source = connection.StartPin.ParentComponent;
                var target = connection.EndPin.ParentComponent;

                if (!adjacency.ContainsKey(source))
                {
                    adjacency[source] = new List<(Component, WaveguideConnection)>();
                }

                adjacency[source].Add((target, connection));

                // Ensure target is also in the adjacency list (even with no outgoing edges)
                if (!adjacency.ContainsKey(target))
                {
                    adjacency[target] = new List<(Component, WaveguideConnection)>();
                }
            }

            return adjacency;
        }
    }
}
