namespace CAP_Core.Analysis
{
    /// <summary>
    /// Aggregated result of all post-simulation sanity checks.
    /// </summary>
    public class SanityCheckResult
    {
        private readonly List<SanityCheckEntry> _entries = new();

        /// <summary>
        /// All sanity check entries.
        /// </summary>
        public IReadOnlyList<SanityCheckEntry> Entries => _entries;

        /// <summary>
        /// True if no errors were found.
        /// </summary>
        public bool IsClean =>
            !_entries.Any(e => e.Severity == SanityCheckSeverity.Error);

        /// <summary>
        /// True if there are any warnings.
        /// </summary>
        public bool HasWarnings =>
            _entries.Any(e => e.Severity == SanityCheckSeverity.Warning);

        /// <summary>
        /// True if there are any errors.
        /// </summary>
        public bool HasErrors =>
            _entries.Any(e => e.Severity == SanityCheckSeverity.Error);

        /// <summary>
        /// Number of error entries.
        /// </summary>
        public int ErrorCount =>
            _entries.Count(e => e.Severity == SanityCheckSeverity.Error);

        /// <summary>
        /// Number of warning entries.
        /// </summary>
        public int WarningCount =>
            _entries.Count(e => e.Severity == SanityCheckSeverity.Warning);

        /// <summary>
        /// Number of info entries.
        /// </summary>
        public int InfoCount =>
            _entries.Count(e => e.Severity == SanityCheckSeverity.Info);

        /// <summary>
        /// Errors from all checks.
        /// </summary>
        public IEnumerable<SanityCheckEntry> Errors =>
            _entries.Where(e => e.Severity == SanityCheckSeverity.Error);

        /// <summary>
        /// Warnings from all checks.
        /// </summary>
        public IEnumerable<SanityCheckEntry> Warnings =>
            _entries.Where(e => e.Severity == SanityCheckSeverity.Warning);

        /// <summary>
        /// Info entries from all checks.
        /// </summary>
        public IEnumerable<SanityCheckEntry> Infos =>
            _entries.Where(e => e.Severity == SanityCheckSeverity.Info);

        /// <summary>
        /// Adds an entry to the result.
        /// </summary>
        public void Add(SanityCheckEntry entry)
        {
            _entries.Add(entry);
        }

        /// <summary>
        /// Adds all entries from another result.
        /// </summary>
        public void AddRange(IEnumerable<SanityCheckEntry> entries)
        {
            _entries.AddRange(entries);
        }
    }
}
