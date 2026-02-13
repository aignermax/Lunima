using CAP_Core.Components.Parametric;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP_DataAccess.Components.ComponentDraftMapper
{
    /// <summary>
    /// Maps PDK S-Matrix draft data into ParametricSMatrix instances.
    /// Handles both fixed-value and formula-based connections.
    /// </summary>
    public static class ParametricSMatrixMapper
    {
        /// <summary>
        /// Returns true if the S-Matrix draft contains parametric formulas.
        /// </summary>
        public static bool IsParametric(PdkSMatrixDraft draft)
        {
            if (draft == null) return false;
            return draft.Parameters?.Count > 0 ||
                   draft.Connections.Any(c => c.IsParametric);
        }

        /// <summary>
        /// Creates a ParametricSMatrix from a PDK S-Matrix draft.
        /// </summary>
        public static ParametricSMatrix MapToParametricSMatrix(
            PdkSMatrixDraft draft)
        {
            if (draft == null)
                throw new ArgumentNullException(nameof(draft));

            var parameters = MapParameters(draft.Parameters);
            var connections = MapConnections(draft.Connections);

            return new ParametricSMatrix(parameters, connections);
        }

        /// <summary>
        /// Validates a parametric S-Matrix draft for correctness.
        /// </summary>
        public static void Validate(
            PdkSMatrixDraft draft,
            string componentName,
            IReadOnlyList<PhysicalPinDraft> pins)
        {
            if (draft.Parameters == null || draft.Parameters.Count == 0)
                return;

            var paramNames = new HashSet<string>();
            foreach (var param in draft.Parameters)
            {
                ValidateParameter(param, componentName, paramNames);
            }

            var pinNames = pins.Select(p => p.Name).ToHashSet();
            ValidateFormulaConnections(
                draft.Connections, componentName, paramNames, pinNames);
        }

        private static List<ParameterDefinition> MapParameters(
            List<ParameterDefinitionDraft>? drafts)
        {
            if (drafts == null || drafts.Count == 0)
                return new List<ParameterDefinition>();

            return drafts.Select(d => new ParameterDefinition(
                d.Name, d.DefaultValue, d.MinValue, d.MaxValue, d.Label
            )).ToList();
        }

        private static List<FormulaConnection> MapConnections(
            List<SMatrixConnection> connections)
        {
            return connections.Select(c =>
            {
                string magFormula = c.MagnitudeFormula
                    ?? c.Magnitude.ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                string phaseFormula = c.PhaseDegreesFormula
                    ?? c.PhaseDegrees.ToString(
                        System.Globalization.CultureInfo.InvariantCulture);

                return new FormulaConnection(
                    c.FromPin, c.ToPin, magFormula, phaseFormula);
            }).ToList();
        }

        private static void ValidateParameter(
            ParameterDefinitionDraft param,
            string componentName,
            HashSet<string> paramNames)
        {
            if (string.IsNullOrWhiteSpace(param.Name))
            {
                throw new InvalidOperationException(
                    $"Parameter in component '{componentName}' must have a name.");
            }

            if (!paramNames.Add(param.Name))
            {
                throw new InvalidOperationException(
                    $"Duplicate parameter '{param.Name}' in component '{componentName}'.");
            }

            if (param.MinValue > param.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Parameter '{param.Name}' in component '{componentName}' " +
                    $"has minValue > maxValue.");
            }
        }

        private static void ValidateFormulaConnections(
            List<SMatrixConnection> connections,
            string componentName,
            HashSet<string> paramNames,
            HashSet<string> pinNames)
        {
            var evaluator = new FormulaEvaluator();

            foreach (var conn in connections)
            {
                if (!pinNames.Contains(conn.FromPin))
                {
                    throw new InvalidOperationException(
                        $"Connection in component '{componentName}' " +
                        $"references unknown pin '{conn.FromPin}'.");
                }
                if (!pinNames.Contains(conn.ToPin))
                {
                    throw new InvalidOperationException(
                        $"Connection in component '{componentName}' " +
                        $"references unknown pin '{conn.ToPin}'.");
                }

                if (!string.IsNullOrWhiteSpace(conn.MagnitudeFormula))
                {
                    if (!evaluator.TryValidate(
                        conn.MagnitudeFormula, paramNames, out string? error))
                    {
                        throw new InvalidOperationException(
                            $"Invalid magnitude formula in component " +
                            $"'{componentName}' ({conn.FromPin}->{conn.ToPin}): {error}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(conn.PhaseDegreesFormula))
                {
                    if (!evaluator.TryValidate(
                        conn.PhaseDegreesFormula, paramNames, out string? error))
                    {
                        throw new InvalidOperationException(
                            $"Invalid phase formula in component " +
                            $"'{componentName}' ({conn.FromPin}->{conn.ToPin}): {error}");
                    }
                }
            }
        }
    }
}
