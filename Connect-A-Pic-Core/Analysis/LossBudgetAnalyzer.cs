using CAP_Core.Components;

namespace CAP_Core.Analysis
{
    /// <summary>
    /// Analyzes system-level loss budgets by finding all input-to-output paths
    /// through waveguide connections and calculating accumulated loss.
    /// </summary>
    public class LossBudgetAnalyzer
    {
        /// <summary>
        /// Maximum path depth to prevent infinite loops in cyclic graphs.
        /// </summary>
        private const int MaxPathDepth = 100;

        /// <summary>
        /// Analyzes all input-to-output paths in the connection graph and
        /// returns a comprehensive loss budget result.
        /// </summary>
        /// <param name="connections">All waveguide connections in the system.</param>
        /// <param name="inputPinIds">Pin IDs that serve as system inputs.</param>
        /// <param name="outputPinIds">Pin IDs that serve as system outputs.</param>
        /// <returns>The complete loss budget analysis.</returns>
        public LossBudgetResult Analyze(
            IReadOnlyList<WaveguideConnection> connections,
            IReadOnlySet<Guid> inputPinIds,
            IReadOnlySet<Guid> outputPinIds)
        {
            if (connections.Count == 0)
                return new LossBudgetResult(Array.Empty<PathLossEntry>(), Array.Empty<WaveguideConnection>());

            var adjacency = BuildAdjacencyMap(connections);
            var allPaths = FindAllPaths(adjacency, inputPinIds, outputPinIds);
            var criticalConnections = FindCriticalConnections(allPaths);

            return new LossBudgetResult(allPaths, criticalConnections);
        }

        /// <summary>
        /// Builds a map from each start pin ID to its outgoing connections.
        /// </summary>
        private static Dictionary<Guid, List<WaveguideConnection>> BuildAdjacencyMap(
            IReadOnlyList<WaveguideConnection> connections)
        {
            var map = new Dictionary<Guid, List<WaveguideConnection>>();

            foreach (var connection in connections)
            {
                var startId = connection.StartPin.PinId;
                if (!map.TryGetValue(startId, out var list))
                {
                    list = new List<WaveguideConnection>();
                    map[startId] = list;
                }
                list.Add(connection);
            }

            return map;
        }

        /// <summary>
        /// Finds all paths from any input pin to any output pin using DFS.
        /// </summary>
        private static List<PathLossEntry> FindAllPaths(
            Dictionary<Guid, List<WaveguideConnection>> adjacency,
            IReadOnlySet<Guid> inputPinIds,
            IReadOnlySet<Guid> outputPinIds)
        {
            var results = new List<PathLossEntry>();

            foreach (var inputPinId in inputPinIds)
            {
                if (!adjacency.ContainsKey(inputPinId))
                    continue;

                var currentPath = new List<WaveguideConnection>();
                var visited = new HashSet<Guid>();
                DfsCollectPaths(
                    inputPinId, adjacency, outputPinIds,
                    currentPath, visited, results);
            }

            return results;
        }

        /// <summary>
        /// Depth-first search collecting all paths from current pin to any output pin.
        /// </summary>
        private static void DfsCollectPaths(
            Guid currentPinId,
            Dictionary<Guid, List<WaveguideConnection>> adjacency,
            IReadOnlySet<Guid> outputPinIds,
            List<WaveguideConnection> currentPath,
            HashSet<Guid> visited,
            List<PathLossEntry> results)
        {
            if (currentPath.Count >= MaxPathDepth)
                return;

            if (!adjacency.TryGetValue(currentPinId, out var outgoing))
                return;

            foreach (var connection in outgoing)
            {
                var endPinId = connection.EndPin.PinId;

                if (visited.Contains(endPinId))
                    continue;

                currentPath.Add(connection);
                visited.Add(endPinId);

                if (outputPinIds.Contains(endPinId))
                {
                    var label = BuildPathLabel(currentPath);
                    results.Add(new PathLossEntry(currentPath.ToList(), label));
                }

                // Continue searching deeper even if we found an output,
                // in case there are further outputs downstream
                DfsCollectPaths(
                    endPinId, adjacency, outputPinIds,
                    currentPath, visited, results);

                currentPath.RemoveAt(currentPath.Count - 1);
                visited.Remove(endPinId);
            }
        }

        /// <summary>
        /// Builds a human-readable label describing the path endpoints.
        /// </summary>
        private static string BuildPathLabel(List<WaveguideConnection> path)
        {
            var first = path[0];
            var last = path[^1];
            var startName = first.StartPin.Name ?? first.StartPin.PinId.ToString()[..8];
            var endName = last.EndPin.Name ?? last.EndPin.PinId.ToString()[..8];
            return $"{startName} -> {endName}";
        }

        /// <summary>
        /// Identifies connections that appear in any High-severity path.
        /// </summary>
        private static List<WaveguideConnection> FindCriticalConnections(
            List<PathLossEntry> paths)
        {
            var critical = new HashSet<WaveguideConnection>();

            foreach (var path in paths)
            {
                if (path.Severity != LossSeverity.High)
                    continue;

                foreach (var connection in path.Connections)
                {
                    critical.Add(connection);
                }
            }

            return critical.ToList();
        }
    }
}
