namespace CAP_Core.Analysis
{
    /// <summary>
    /// Contains the results of feedback loop detection and analysis.
    /// </summary>
    public class FeedbackLoopAnalysisResult
    {
        private readonly List<FeedbackLoop> _loops = new();
        private readonly List<string> _warnings = new();

        /// <summary>
        /// All detected feedback loops.
        /// </summary>
        public IReadOnlyList<FeedbackLoop> Loops => _loops;

        /// <summary>
        /// Instability and diagnostic warnings.
        /// </summary>
        public IReadOnlyList<string> Warnings => _warnings;

        /// <summary>
        /// Total number of detected feedback loops.
        /// </summary>
        public int LoopCount => _loops.Count;

        /// <summary>
        /// True if any detected loop has gain magnitude >= 1.0.
        /// </summary>
        public bool HasUnstableLoops => _loops.Any(l => l.IsUnstable);

        /// <summary>
        /// Number of loops with gain magnitude >= 1.0.
        /// </summary>
        public int UnstableLoopCount => _loops.Count(l => l.IsUnstable);

        /// <summary>
        /// Number of stable (gain &lt; 1.0) loops.
        /// </summary>
        public int StableLoopCount => _loops.Count(l => !l.IsUnstable);

        /// <summary>
        /// Adds a detected feedback loop to the results.
        /// </summary>
        public void AddLoop(FeedbackLoop loop)
        {
            _loops.Add(loop);
        }

        /// <summary>
        /// Adds a warning message.
        /// </summary>
        public void AddWarning(string message)
        {
            _warnings.Add(message);
        }
    }
}
