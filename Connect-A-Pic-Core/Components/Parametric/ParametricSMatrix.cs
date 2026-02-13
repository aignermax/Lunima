using System.Numerics;

namespace CAP_Core.Components.Parametric
{
    /// <summary>
    /// Defines S-Matrix connections using parametric formulas.
    /// Parameters can be varied at runtime to recompute the S-Matrix
    /// without redefining the component structure.
    /// </summary>
    public class ParametricSMatrix
    {
        private readonly FormulaEvaluator _evaluator = new();
        private readonly Dictionary<string, double> _currentValues = new();
        private readonly List<ParameterDefinition> _parameters;
        private readonly List<FormulaConnection> _connections;

        /// <summary>
        /// Read-only access to the parameter definitions.
        /// </summary>
        public IReadOnlyList<ParameterDefinition> Parameters => _parameters;

        /// <summary>
        /// Read-only access to the formula connections.
        /// </summary>
        public IReadOnlyList<FormulaConnection> Connections => _connections;

        /// <summary>
        /// Raised when any parameter value changes.
        /// </summary>
        public event EventHandler? ParameterChanged;

        /// <summary>
        /// Creates a new parametric S-Matrix template.
        /// </summary>
        /// <param name="parameters">Parameter definitions for this template.</param>
        /// <param name="connections">Formula-based connections.</param>
        public ParametricSMatrix(
            IEnumerable<ParameterDefinition> parameters,
            IEnumerable<FormulaConnection> connections)
        {
            _parameters = parameters?.ToList()
                ?? throw new ArgumentNullException(nameof(parameters));
            _connections = connections?.ToList()
                ?? throw new ArgumentNullException(nameof(connections));

            foreach (var param in _parameters)
            {
                _currentValues[param.Name] = param.DefaultValue;
            }

            ValidateFormulas();
        }

        /// <summary>
        /// Gets the current value of a named parameter.
        /// </summary>
        public double GetParameterValue(string parameterName)
        {
            if (!_currentValues.TryGetValue(parameterName, out double value))
                throw new ArgumentException(
                    $"Unknown parameter: '{parameterName}'.");
            return value;
        }

        /// <summary>
        /// Sets a parameter value, clamping to its defined range.
        /// </summary>
        public void SetParameterValue(string parameterName, double value)
        {
            var definition = _parameters.FirstOrDefault(
                p => p.Name == parameterName)
                ?? throw new ArgumentException(
                    $"Unknown parameter: '{parameterName}'.");

            double clamped = Math.Clamp(value, definition.MinValue, definition.MaxValue);
            if (_currentValues[parameterName] == clamped)
                return;

            _currentValues[parameterName] = clamped;
            ParameterChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Evaluates all connections with current parameter values.
        /// Returns a list of (fromPin, toPin, complexValue) tuples.
        /// </summary>
        public List<EvaluatedConnection> EvaluateConnections()
        {
            var results = new List<EvaluatedConnection>();

            foreach (var conn in _connections)
            {
                double magnitude = _evaluator.Evaluate(
                    conn.MagnitudeFormula, _currentValues);
                double phaseDeg = _evaluator.Evaluate(
                    conn.PhaseDegFormula, _currentValues);

                double phaseRad = phaseDeg * Math.PI / 180.0;
                var complexValue = Complex.FromPolarCoordinates(magnitude, phaseRad);

                results.Add(new EvaluatedConnection(
                    conn.FromPin, conn.ToPin, complexValue));
            }

            return results;
        }

        private void ValidateFormulas()
        {
            var paramNames = _parameters.Select(p => p.Name).ToHashSet();

            foreach (var conn in _connections)
            {
                if (!_evaluator.TryValidate(conn.MagnitudeFormula, paramNames, out string? magError))
                {
                    throw new InvalidOperationException(
                        $"Invalid magnitude formula for {conn.FromPin}->{conn.ToPin}: {magError}");
                }

                if (!_evaluator.TryValidate(conn.PhaseDegFormula, paramNames, out string? phaseError))
                {
                    throw new InvalidOperationException(
                        $"Invalid phase formula for {conn.FromPin}->{conn.ToPin}: {phaseError}");
                }
            }
        }
    }
}
