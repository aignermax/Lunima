using System.Numerics;
using CAP_Core.Components;

namespace CAP_Core.Analysis
{
    /// <summary>
    /// Represents a detected feedback loop (cycle) in the photonic network.
    /// A feedback loop is an ordered sequence of components and connections
    /// that form a closed path where light can circulate.
    /// </summary>
    public class FeedbackLoop
    {
        /// <summary>
        /// Ordered list of components forming the cycle.
        /// The last component connects back to the first.
        /// </summary>
        public IReadOnlyList<Component> Components { get; }

        /// <summary>
        /// Ordered list of waveguide connections forming the cycle edges.
        /// Connection[i] goes from Components[i] to Components[(i+1) % Count].
        /// </summary>
        public IReadOnlyList<WaveguideConnection> Connections { get; }

        /// <summary>
        /// Product of all transmission coefficients around the loop.
        /// Includes both waveguide connection losses and component internal transfers.
        /// </summary>
        public Complex LoopGain { get; }

        /// <summary>
        /// Magnitude of the loop gain. Values >= 1.0 indicate potential instability.
        /// </summary>
        public double LoopGainMagnitude => LoopGain.Magnitude;

        /// <summary>
        /// True if the loop gain magnitude is >= 1.0, indicating potential oscillation.
        /// </summary>
        public bool IsUnstable => LoopGainMagnitude >= UnstableGainThreshold;

        /// <summary>
        /// Number of components in this loop.
        /// </summary>
        public int Length => Components.Count;

        /// <summary>
        /// Threshold above which a loop is considered potentially unstable.
        /// </summary>
        public const double UnstableGainThreshold = 1.0;

        /// <summary>
        /// Creates a new FeedbackLoop.
        /// </summary>
        /// <param name="components">Ordered components forming the cycle.</param>
        /// <param name="connections">Ordered connections forming the cycle edges.</param>
        /// <param name="loopGain">Calculated round-trip gain.</param>
        public FeedbackLoop(
            IReadOnlyList<Component> components,
            IReadOnlyList<WaveguideConnection> connections,
            Complex loopGain)
        {
            Components = components;
            Connections = connections;
            LoopGain = loopGain;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var names = string.Join(" -> ", Components.Select(c => c.Identifier));
            return $"Loop({Length}): {names} | Gain={LoopGainMagnitude:F4}";
        }
    }
}
