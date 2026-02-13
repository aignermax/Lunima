using System.Numerics;
using CAP_Core.Components;
using CAP_Core.LightCalculation;

namespace CAP_Core.Analysis
{
    /// <summary>
    /// Detects feedback loops in a photonic network using DFS-based cycle detection.
    /// Calculates loop gain as the product of transmission coefficients around each cycle
    /// and warns about potential instability when gain >= 1.
    /// </summary>
    public class FeedbackLoopDetector : IFeedbackLoopDetector
    {
        /// <summary>
        /// Maximum number of loops to detect before stopping.
        /// Prevents excessive computation on highly connected networks.
        /// </summary>
        public const int MaxLoopsToDetect = 1000;

        /// <inheritdoc />
        public FeedbackLoopAnalysisResult Analyze(
            IReadOnlyList<Component> components,
            IReadOnlyList<WaveguideConnection> connections,
            int wavelengthNm)
        {
            var result = new FeedbackLoopAnalysisResult();

            if (components.Count == 0 || connections.Count == 0)
            {
                return result;
            }

            var adjacency = NetworkGraphBuilder.BuildAdjacencyList(connections);
            var cycles = DetectCycles(adjacency);

            foreach (var cycle in cycles)
            {
                var loopConnections = ResolveLoopConnections(cycle, adjacency);
                var loopGain = CalculateLoopGain(cycle, loopConnections, wavelengthNm);
                var loop = new FeedbackLoop(cycle, loopConnections, loopGain);
                result.AddLoop(loop);
            }

            GenerateWarnings(result);
            return result;
        }

        /// <summary>
        /// Detects all simple cycles in the directed graph using DFS.
        /// </summary>
        internal static List<List<Component>> DetectCycles(
            Dictionary<Component, List<(Component Target, WaveguideConnection Connection)>> adjacency)
        {
            var cycles = new List<List<Component>>();
            var visited = new HashSet<Component>();
            var stack = new HashSet<Component>();
            var path = new List<Component>();

            foreach (var node in adjacency.Keys)
            {
                if (cycles.Count >= MaxLoopsToDetect) break;
                if (!visited.Contains(node))
                {
                    DfsFindCycles(node, adjacency, visited, stack, path, cycles);
                }
            }

            return cycles;
        }

        private static void DfsFindCycles(
            Component current,
            Dictionary<Component, List<(Component Target, WaveguideConnection Connection)>> adjacency,
            HashSet<Component> visited,
            HashSet<Component> stack,
            List<Component> path,
            List<List<Component>> cycles)
        {
            if (cycles.Count >= MaxLoopsToDetect) return;

            visited.Add(current);
            stack.Add(current);
            path.Add(current);

            if (adjacency.TryGetValue(current, out var neighbors))
            {
                foreach (var (target, _) in neighbors)
                {
                    if (cycles.Count >= MaxLoopsToDetect) break;

                    if (stack.Contains(target))
                    {
                        // Found a cycle: extract from target's position in path to end
                        var cycleStart = path.IndexOf(target);
                        var cycle = path.GetRange(cycleStart, path.Count - cycleStart);
                        if (!IsDuplicateCycle(cycle, cycles))
                        {
                            cycles.Add(cycle);
                        }
                    }
                    else if (!visited.Contains(target))
                    {
                        DfsFindCycles(target, adjacency, visited, stack, path, cycles);
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            stack.Remove(current);
        }

        /// <summary>
        /// Checks if a cycle is a rotation of an already-detected cycle.
        /// </summary>
        private static bool IsDuplicateCycle(
            List<Component> candidate, List<List<Component>> existing)
        {
            foreach (var cycle in existing)
            {
                if (cycle.Count != candidate.Count) continue;
                if (AreCyclicPermutations(cycle, candidate)) return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if two sequences are cyclic permutations of each other.
        /// </summary>
        internal static bool AreCyclicPermutations<T>(List<T> a, List<T> b)
        {
            if (a.Count != b.Count) return false;
            if (a.Count == 0) return true;

            for (int offset = 0; offset < a.Count; offset++)
            {
                bool match = true;
                for (int i = 0; i < a.Count; i++)
                {
                    if (!EqualityComparer<T>.Default.Equals(
                        a[i], b[(i + offset) % b.Count]))
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return true;
            }
            return false;
        }

        /// <summary>
        /// Resolves the waveguide connections for each edge in the cycle.
        /// </summary>
        private static List<WaveguideConnection> ResolveLoopConnections(
            List<Component> cycle,
            Dictionary<Component, List<(Component Target, WaveguideConnection Connection)>> adjacency)
        {
            var connections = new List<WaveguideConnection>();
            for (int i = 0; i < cycle.Count; i++)
            {
                var source = cycle[i];
                var target = cycle[(i + 1) % cycle.Count];

                var edge = adjacency[source]
                    .FirstOrDefault(e => e.Target == target);
                connections.Add(edge.Connection);
            }
            return connections;
        }

        /// <summary>
        /// Calculates the round-trip loop gain as the product of all
        /// waveguide transmission coefficients around the cycle.
        /// </summary>
        internal static Complex CalculateLoopGain(
            List<Component> cycle,
            List<WaveguideConnection> connections,
            int wavelengthNm)
        {
            var gain = Complex.One;

            // Multiply waveguide connection transmission coefficients
            foreach (var connection in connections)
            {
                if (connection != null)
                {
                    gain *= connection.TransmissionCoefficient;
                }
            }

            // Multiply component internal transfer for each hop through a component
            for (int i = 0; i < cycle.Count; i++)
            {
                var component = cycle[i];
                var inConnection = connections[(i - 1 + connections.Count) % connections.Count];
                var outConnection = connections[i];

                var internalTransfer = GetComponentInternalTransfer(
                    component, inConnection, outConnection, wavelengthNm);
                gain *= internalTransfer;
            }

            return gain;
        }

        /// <summary>
        /// Gets the S-matrix transfer coefficient for light passing through a component
        /// from the input pin to the output pin.
        /// </summary>
        private static Complex GetComponentInternalTransfer(
            Component component,
            WaveguideConnection inConnection,
            WaveguideConnection outConnection,
            int wavelengthNm)
        {
            if (inConnection?.EndPin?.LogicalPin == null ||
                outConnection?.StartPin?.LogicalPin == null)
            {
                return Complex.One;
            }

            if (!component.WaveLengthToSMatrixMap.TryGetValue(wavelengthNm, out var sMatrix))
            {
                return Complex.One;
            }

            var inPinId = inConnection.EndPin.LogicalPin.IDInFlow;
            var outPinId = outConnection.StartPin.LogicalPin.IDOutFlow;

            if (!sMatrix.PinReference.TryGetValue(inPinId, out int inIndex) ||
                !sMatrix.PinReference.TryGetValue(outPinId, out int outIndex))
            {
                return Complex.One;
            }

            return sMatrix.SMat[outIndex, inIndex];
        }

        /// <summary>
        /// Generates warning messages for unstable loops.
        /// </summary>
        private static void GenerateWarnings(FeedbackLoopAnalysisResult result)
        {
            foreach (var loop in result.Loops)
            {
                if (loop.IsUnstable)
                {
                    var names = string.Join(" -> ", loop.Components.Select(c => c.Identifier));
                    result.AddWarning(
                        $"Unstable feedback loop detected: {names}. " +
                        $"Loop gain magnitude = {loop.LoopGainMagnitude:F4} >= 1.0. " +
                        $"This circuit may oscillate.");
                }
            }

            if (result.HasUnstableLoops)
            {
                result.AddWarning(
                    $"Network contains {result.UnstableLoopCount} potentially unstable " +
                    $"feedback loop(s). Review loop gains to ensure stability.");
            }
        }
    }
}
