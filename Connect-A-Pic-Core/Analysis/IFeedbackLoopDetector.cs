using CAP_Core.Components;

namespace CAP_Core.Analysis
{
    /// <summary>
    /// Detects and analyzes feedback loops in a photonic network.
    /// </summary>
    public interface IFeedbackLoopDetector
    {
        /// <summary>
        /// Analyzes the network for feedback loops.
        /// </summary>
        /// <param name="components">All components in the network.</param>
        /// <param name="connections">All waveguide connections between components.</param>
        /// <param name="wavelengthNm">Wavelength in nm for S-matrix lookup.</param>
        /// <returns>Analysis result containing all detected loops and warnings.</returns>
        FeedbackLoopAnalysisResult Analyze(
            IReadOnlyList<Component> components,
            IReadOnlyList<WaveguideConnection> connections,
            int wavelengthNm);
    }
}
