using CAP_Core.LightCalculation.Validation;

namespace CAP_Core.Analysis
{
    /// <summary>
    /// Surfaces energy conservation violations from the S-Matrix validator.
    /// Translates SMatrixValidationResult entries into SanityCheckEntries.
    /// </summary>
    public class EnergyConservationCheck : ISanityCheck
    {
        private const string Category = "Energy Conservation";

        /// <inheritdoc />
        public IEnumerable<SanityCheckEntry> Run(SanityCheckContext context)
        {
            if (context.ValidationResult == null)
            {
                yield return new SanityCheckEntry(
                    SanityCheckSeverity.Info,
                    Category,
                    "No S-Matrix validation result available.");
                yield break;
            }

            foreach (var entry in context.ValidationResult.Entries)
            {
                var severity = MapSeverity(entry.Severity);

                yield return new SanityCheckEntry(
                    severity,
                    Category,
                    entry.Message);
            }
        }

        private static SanityCheckSeverity MapSeverity(
            SMatrixValidationSeverity source)
        {
            return source switch
            {
                SMatrixValidationSeverity.Error => SanityCheckSeverity.Error,
                SMatrixValidationSeverity.Warning => SanityCheckSeverity.Warning,
                _ => SanityCheckSeverity.Info,
            };
        }
    }
}
