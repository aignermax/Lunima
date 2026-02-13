namespace CAP_Core.LightCalculation.Validation
{
    /// <summary>
    /// Contains the structured results of an S-Matrix validation.
    /// </summary>
    public class SMatrixValidationResult
    {
        private readonly List<SMatrixValidationEntry> _entries = new();

        /// <summary>
        /// All validation entries (warnings and errors).
        /// </summary>
        public IReadOnlyList<SMatrixValidationEntry> Entries => _entries;

        /// <summary>
        /// True if the matrix passed validation with no errors.
        /// </summary>
        public bool IsValid => !_entries.Any(e => e.Severity == SMatrixValidationSeverity.Error);

        /// <summary>
        /// True if there are any warnings.
        /// </summary>
        public bool HasWarnings => _entries.Any(e => e.Severity == SMatrixValidationSeverity.Warning);

        /// <summary>
        /// All error-level entries.
        /// </summary>
        public IEnumerable<SMatrixValidationEntry> Errors =>
            _entries.Where(e => e.Severity == SMatrixValidationSeverity.Error);

        /// <summary>
        /// All warning-level entries.
        /// </summary>
        public IEnumerable<SMatrixValidationEntry> Warnings =>
            _entries.Where(e => e.Severity == SMatrixValidationSeverity.Warning);

        /// <summary>
        /// Adds a validation entry.
        /// </summary>
        /// <param name="entry">The entry to add.</param>
        public void Add(SMatrixValidationEntry entry)
        {
            _entries.Add(entry);
        }

        /// <summary>
        /// Adds a warning entry.
        /// </summary>
        /// <param name="message">The warning message.</param>
        public void AddWarning(string message)
        {
            _entries.Add(new SMatrixValidationEntry(SMatrixValidationSeverity.Warning, message));
        }

        /// <summary>
        /// Adds an error entry.
        /// </summary>
        /// <param name="message">The error message.</param>
        public void AddError(string message)
        {
            _entries.Add(new SMatrixValidationEntry(SMatrixValidationSeverity.Error, message));
        }
    }
}
