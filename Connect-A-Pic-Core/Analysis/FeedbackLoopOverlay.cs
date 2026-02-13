using CAP_Core.Components;

namespace CAP_Core.Analysis
{
    /// <summary>
    /// Provides visualization data for highlighting feedback loops on the canvas.
    /// Contains the component and connection identifiers that participate in loops.
    /// </summary>
    public class FeedbackLoopOverlay
    {
        /// <summary>
        /// Components that are part of at least one feedback loop.
        /// </summary>
        public IReadOnlySet<Component> LoopComponents { get; }

        /// <summary>
        /// Waveguide connections that are part of at least one feedback loop.
        /// </summary>
        public IReadOnlySet<WaveguideConnection> LoopConnections { get; }

        /// <summary>
        /// Components that participate in at least one unstable loop (gain >= 1).
        /// </summary>
        public IReadOnlySet<Component> UnstableComponents { get; }

        /// <summary>
        /// Connections that participate in at least one unstable loop (gain >= 1).
        /// </summary>
        public IReadOnlySet<WaveguideConnection> UnstableConnections { get; }

        /// <summary>
        /// Creates a FeedbackLoopOverlay from analysis results.
        /// </summary>
        /// <param name="result">The feedback loop analysis result.</param>
        public FeedbackLoopOverlay(FeedbackLoopAnalysisResult result)
        {
            var loopComponents = new HashSet<Component>();
            var loopConnections = new HashSet<WaveguideConnection>();
            var unstableComponents = new HashSet<Component>();
            var unstableConnections = new HashSet<WaveguideConnection>();

            foreach (var loop in result.Loops)
            {
                foreach (var component in loop.Components)
                {
                    loopComponents.Add(component);
                    if (loop.IsUnstable)
                    {
                        unstableComponents.Add(component);
                    }
                }

                foreach (var connection in loop.Connections)
                {
                    if (connection != null)
                    {
                        loopConnections.Add(connection);
                        if (loop.IsUnstable)
                        {
                            unstableConnections.Add(connection);
                        }
                    }
                }
            }

            LoopComponents = loopComponents;
            LoopConnections = loopConnections;
            UnstableComponents = unstableComponents;
            UnstableConnections = unstableConnections;
        }

        /// <summary>
        /// Returns true if the given component participates in any feedback loop.
        /// </summary>
        public bool IsInLoop(Component component) => LoopComponents.Contains(component);

        /// <summary>
        /// Returns true if the given connection participates in any feedback loop.
        /// </summary>
        public bool IsInLoop(WaveguideConnection connection) =>
            LoopConnections.Contains(connection);

        /// <summary>
        /// Returns true if the given component participates in an unstable loop.
        /// </summary>
        public bool IsUnstable(Component component) =>
            UnstableComponents.Contains(component);

        /// <summary>
        /// Returns true if the given connection participates in an unstable loop.
        /// </summary>
        public bool IsUnstable(WaveguideConnection connection) =>
            UnstableConnections.Contains(connection);
    }
}
